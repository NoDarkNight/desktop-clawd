using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace DesktopClawd;

/// <summary>
/// A small borderless, transparent, always-on-top window that hosts the pet.
/// Behaviour lives in <see cref="PetBrain"/>; this class only bridges it to Avalonia.
/// </summary>
public sealed class PetWindow : Window, IPetHost
{
    private const double PixelScale = 4;

    private readonly SpriteLibrary _sprites;
    private readonly ClawdView _view;
    private readonly DispatcherTimer _timer;
    private readonly PetBrain _brain;

    public PetWindow(SpriteLibrary sprites)
    {
        _sprites = sprites;
        _view = new ClawdView(sprites) { Scale = PixelScale };

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

        _brain = new PetBrain(this);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());

        Opened += (_, _) =>
        {
            var area = (Screens.Primary ?? Screens.All[0]).WorkingArea;
            _brain.Start(new WorkArea(area.X, area.Y, area.Right, area.Bottom));
            ApplyPosition();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public double Scale => RenderScaling;
    public double PxWidth => Width * RenderScaling;
    public double PxHeight => Height * RenderScaling;

    public WorkArea WorkAreaAt(double x, double y)
    {
        var screen = Screens.ScreenFromPoint(new PixelPoint((int)x, (int)y))
                     ?? Screens.ScreenFromWindow(this)
                     ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        return new WorkArea(area.X, area.Y, area.Right, area.Bottom);
    }

    public void OnClaude(ClaudeActivity activity) => _brain.OnClaude(activity);

    /// <summary>Re-reads the sprites folder; the window resizes if the canvas changed.</summary>
    public void ReloadSprites()
    {
        _sprites.Reload();
        ApplyCanvasSize();
        Icon = _sprites.CreateIcon();
        _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft, force: true);
    }

    private void ApplyCanvasSize()
    {
        Width = _sprites.Canvas.Width * PixelScale;
        Height = _sprites.Canvas.Height * PixelScale;
    }

    private void Tick()
    {
        _brain.Tick();
        _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft);
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        var target = new PixelPoint((int)Math.Round(_brain.X), (int)Math.Round(_brain.Y));
        if (Position != target) Position = target;
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
            _view.Show(_brain.Animation, _brain.AnimTime, _brain.FacingLeft);
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
