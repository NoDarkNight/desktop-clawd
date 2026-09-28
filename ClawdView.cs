using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DesktopClawd;

/// <summary>
/// Draws the current animation frame on a canvas inset by <see cref="EffectMargin"/> art pixels at the
/// sides and <see cref="Headroom"/> above, leaving room for effects around the character.
/// </summary>
public sealed class ClawdView : Control
{
    private readonly SpriteLibrary _sprites;
    private readonly EffectsLayer _effects;
    private SpriteFrame? _frame;
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
    public int EffectMargin { get; init; } = 8;
    public int Headroom { get; init; } = 16;

    private Point CanvasOrigin => new(EffectMargin * Scale, Headroom * Scale);

    public void Show(Anim anim, double animTime, bool flip, PetEffect effect, double dt, bool force = false)
    {
        var frame = _sprites.FrameAt(anim, animTime);
        var changed = force || frame != _frame || flip != _flip;
        _frame = frame;
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
        _sprites.Draw(context, frame, Scale, _flip, CanvasOrigin);
        _effects.Draw(context, Scale, CanvasOrigin, _headX, _headY);
    }
}
