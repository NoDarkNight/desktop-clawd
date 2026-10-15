using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DesktopClawd;

/// <summary>
/// Draws the current animation frame, any held object, and effects, on a canvas inset by
/// <see cref="SideMargin"/> art pixels at the sides and <see cref="Headroom"/> above.
/// </summary>
public sealed class ClawdView : Control
{
    private readonly SpriteLibrary _sprites;
    private readonly EffectsLayer _effects;
    private SpriteFrame? _frame, _object;
    private bool _flip;
    private double _headX, _headY;

    public ClawdView(SpriteLibrary sprites)
    {
        _sprites = sprites;
        _effects = new EffectsLayer(sprites);
        // Nearest-neighbour scaling keeps the pixel art crisp.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public double Scale { get; init; } = 4;
    public int SideMargin { get; set; } = 8;
    public int Headroom { get; init; } = 20;

    private Point CanvasOrigin => new(SideMargin * Scale, Headroom * Scale);

    public void Show(Anim anim, double animTime, bool flip, PetEffect effect, string? emote, double dt, bool force = false)
    {
        // Emotes may contain text-like art (numbers, letters), so they're never mirrored.
        var emoteFrame = emote is null ? null : _sprites.EmoteFrameAt(emote, animTime);
        var frame = emoteFrame ?? _sprites.FrameAt(anim, animTime);
        var obj = emoteFrame is null ? _sprites.HeldObjectAt(anim, animTime) : null;
        if (emoteFrame is not null) flip = false;

        var changed = force || frame != _frame || obj != _object || flip != _flip;
        _frame = frame;
        _object = obj;
        _flip = flip;

        var (_, top) = _sprites.Placement(frame);
        _headX = _sprites.Canvas.Width / 2.0;
        _headY = top + frame.TopRow;
        _effects.Update(effect, dt, flip, _headX, _headY);

        if (changed || _effects.IsActive) InvalidateVisual();
    }

    /// <summary>Whether a point in canvas art pixels lands on an opaque pixel of the character.</summary>
    public bool HitTestCanvas(double x, double y) => _frame is { } frame && _sprites.HitTest(frame, _flip, x, y);

    public override void Render(DrawingContext context)
    {
        if (_frame is not { } frame) return;
        var origin = CanvasOrigin;
        var behind = frame.MarkerBehind;

        if (_object is { } back && behind) _sprites.DrawHeldObject(context, frame, back, Scale, _flip, origin);
        _sprites.Draw(context, frame, Scale, _flip, origin);
        if (_object is { } front && !behind) _sprites.DrawHeldObject(context, frame, front, Scale, _flip, origin);

        _effects.Draw(context, Scale, origin, _headX, _headY);
    }
}
