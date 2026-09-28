using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DesktopClawd;

/// <summary>Draws the current animation frame.</summary>
public sealed class ClawdView : Control
{
    private readonly SpriteLibrary _sprites;
    private SpriteFrame? _frame;
    private bool _flip;

    public ClawdView(SpriteLibrary sprites)
    {
        _sprites = sprites;
        // Nearest-neighbour scaling keeps the pixel art crisp.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public double Scale { get; init; } = 4;

    public void Show(Anim anim, double animTime, bool flip, bool force = false)
    {
        var frame = _sprites.FrameAt(anim, animTime);
        if (!force && frame == _frame && flip == _flip) return;
        _frame = frame;
        _flip = flip;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_frame is { } frame) _sprites.Draw(context, frame, Scale, _flip);
    }
}
