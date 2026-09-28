using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace DesktopClawd;

/// <summary>
/// A borderless, transparent, always-on-top window that hosts the pet. Behaviour lives in
/// <see cref="PetBrain"/>; this class bridges it to Avalonia and the desktop. The window is
/// larger than the character to leave room for effects, and passes clicks through everywhere
/// except the character's opaque pixels.
/// </summary>
public sealed class PetWindow : Window, IPetHost
{
    private const double PixelScale = 4;     // DIPs per art pixel
    private const int EffectMargin = 8;      // art px either side of the character
    private const int EffectHeadroom = 20;   // art px above the character
    private const double ScanInterval = 0.1; // s between window scans

    private readonly SpriteLibrary _sprites;
    private readonly ClawdView _view;
    private readonly DispatcherTimer _timer;
    private readonly PetBrain _brain;
    private readonly IDesktopEnvironment _desktop =
        OperatingSystem.IsWindows() ? new WindowsDesktop() : new NullDesktop();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IReadOnlyList<Surface> _surfaces = [];
    private double _lastTick, _lastScan = double.MinValue;
    private bool? _clickThrough;

    public PetWindow(SpriteLibrary sprites)
    {
        _sprites = sprites;
        _view = new ClawdView(sprites) { Scale = PixelScale, EffectMargin = EffectMargin, Headroom = EffectHeadroom };

        Title = "Desktop Clawd";
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _view;
        ApplyCanvasSize();

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        ContextMenu = new ContextMenu { Items = { quit } };

        // CLAWD_SLEEP_AFTER=<seconds> overrides how long you must be idle before it naps.
        _brain = double.TryParse(Environment.GetEnvironmentVariable("CLAWD_SLEEP_AFTER"), out var sleepAfter)
            ? new PetBrain(this) { SleepAfterIdle = sleepAfter }
            : new PetBrain(this);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());

        Opened += (_, _) =>
        {
            var area = (Screens.Primary ?? Screens.All[0]).WorkingArea;
            _brain.Start(new WorkArea(area.X, area.Y, area.Right, area.Bottom));
            ApplyPosition();
            _lastTick = _clock.Elapsed.TotalSeconds;
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    // --- IPetHost ---

    public double Scale => RenderScaling;
    public double PxWidth => _sprites.Canvas.Width * PixelScale * RenderScaling;
    public double PxHeight => _sprites.Canvas.Height * PixelScale * RenderScaling;
    public IReadOnlyList<Surface> Surfaces => _surfaces;
    public (double X, double Y)? CursorPosition => _desktop.CursorPosition;
    public TimeSpan? UserIdle => _desktop.UserIdleTime;

    public bool TryGetWindowOrigin(nint id, out double left, out double top) =>
        _desktop.TryGetWindowOrigin(id, out left, out top);

    public WorkArea WorkAreaAt(double x, double y)
    {
        var screen = Screens.ScreenFromPoint(new PixelPoint((int)x, (int)y))
                     ?? Screens.ScreenFromWindow(this)
                     ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        return new WorkArea(area.X, area.Y, area.Right, area.Bottom);
    }

    // ---

    public void OnClaude(ClaudeActivity activity) => _brain.OnClaude(activity);

    /// <summary>Re-reads the sprites folder; the window resizes if the canvas changed.</summary>
    public void ReloadSprites()
    {
        _sprites.Reload();
        ApplyCanvasSize();
        Icon = _sprites.CreateIcon();
        _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft, _brain.Effect, 0, force: true);
    }

    private void ApplyCanvasSize()
    {
        Width = (_sprites.Canvas.Width + 2 * EffectMargin) * PixelScale;
        Height = (_sprites.Canvas.Height + EffectHeadroom) * PixelScale;
    }

    private void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _lastTick, 0.1);
        _lastTick = now;

        if (now - _lastScan >= ScanInterval)
        {
            _surfaces = _desktop.ScanSurfaces();
            _lastScan = now;
        }

        _brain.Tick();
        _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft, _brain.Effect, dt);
        ApplyPosition();
        UpdateClickThrough();
    }

    /// <summary>The window's top-left is offset from the character canvas by the effect margins.</summary>
    private void ApplyPosition()
    {
        var art = PixelScale * RenderScaling;
        var target = new PixelPoint(
            (int)Math.Round(_brain.X - EffectMargin * art),
            (int)Math.Round(_brain.Y - EffectHeadroom * art));
        if (Position != target) Position = target;
    }

    /// <summary>Only the character's opaque pixels take the mouse; everything else passes through.</summary>
    private void UpdateClickThrough()
    {
        if (TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == 0) return;

        var hittable = _brain.IsPressed;
        if (!hittable && _desktop.CursorPosition is { } cursor)
        {
            var art = PixelScale * RenderScaling;
            hittable = _view.HitTestCanvas((cursor.X - _brain.X) / art, (cursor.Y - _brain.Y) / art);
        }

        if (_clickThrough == !hittable) return;
        _clickThrough = !hittable;
        _desktop.SetClickThrough(hwnd, !hittable);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var screen = this.PointToScreen(point.Position);
        _brain.Press(screen.X, screen.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var screen = this.PointToScreen(e.GetPosition(this));
        if (_brain.Move(screen.X, screen.Y))
        {
            _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft, _brain.Effect, 0);
            ApplyPosition();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        _brain.Release();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _brain.Release();
    }
}
