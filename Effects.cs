using Avalonia;
using Avalonia.Media;

namespace DesktopClawd;

/// <summary>
/// Overlay effects (sleeping Zs, alert "!", sparkles, dizzy stars) drawn around the character.
/// Coordinates are art pixels relative to the character canvas; glyphs are never mirrored.
/// </summary>
public sealed class EffectsLayer(SpriteLibrary sprites)
{
    private readonly Random _rng = new();
    private readonly List<Particle> _particles = [];
    private PetEffect _effect;
    private double _time, _spawnIn;
    private int _spawned;

    private record struct Particle(Fx Glyph, double X, double Y, double Vx, double Vy, double Age, double Life);

    /// <summary>True while anything is on screen, so the view keeps redrawing.</summary>
    public bool IsActive => _effect != PetEffect.None || _particles.Count > 0;

    /// <param name="headX">Horizontal centre of the character, in canvas art pixels.</param>
    /// <param name="headY">Top of the character's head, in canvas art pixels.</param>
    public void Update(PetEffect effect, double dt, bool facingLeft, double headX, double headY)
    {
        _time += dt;
        if (effect != _effect)
        {
            _effect = effect;
            _spawnIn = 0;
        }

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p = p with { X = p.X + p.Vx * dt, Y = p.Y + p.Vy * dt, Age = p.Age + dt };
            if (p.Age >= p.Life) _particles.RemoveAt(i);
            else _particles[i] = p;
        }

        if ((_spawnIn -= dt) > 0) return;
        switch (effect)
        {
            case PetEffect.Sleeping:
                // Zs drift up and away from the face, alternating small and large.
                var dir = facingLeft ? -1 : 1;
                var glyph = _spawned++ % 2 == 0 ? Fx.ZSmall : Fx.Z;
                _particles.Add(new Particle(glyph, headX + dir * 5, headY - 5, dir * 2, -5, 0, 2.4));
                _spawnIn = 1.2;
                break;
            case PetEffect.Sparkles:
                _particles.Add(new Particle(Fx.Sparkle,
                    headX + (_rng.NextDouble() * 2 - 1) * 10, headY + (_rng.NextDouble() * 2 - 1) * 4,
                    0, -5, 0, 0.5));
                _spawnIn = 0.1;
                break;
        }
    }

    public void Draw(DrawingContext ctx, double scale, Point canvasOrigin, double headX, double headY)
    {
        foreach (var p in _particles)
        {
            var t = p.Age / p.Life;
            var opacity = t < 0.6 ? 1 : 1 - (t - 0.6) / 0.4;
            using (ctx.PushOpacity(opacity))
                DrawGlyph(ctx, p.Glyph, p.X, p.Y, scale, canvasOrigin);
        }

        switch (_effect)
        {
            case PetEffect.Alert:
            {
                // Bobbing "!" above the head.
                var glyph = sprites.Effect(Fx.Alert);
                var bob = Math.Sin(_time * 8) > 0 ? 0 : 1;
                DrawGlyph(ctx, Fx.Alert, headX, headY - glyph.Source.Height / 2.0 - 2 - bob, scale, canvasOrigin);
                break;
            }
            case PetEffect.Dizzy:
                // Three stars circling the head.
                for (var k = 0; k < 3; k++)
                {
                    var angle = _time * Math.Tau * 1.1 + k * Math.Tau / 3;
                    DrawGlyph(ctx, Fx.Star, headX + Math.Cos(angle) * 8, headY - 3 + Math.Sin(angle) * 2, scale, canvasOrigin);
                }
                break;
        }
    }

    /// <summary>Draws a glyph centred on (x, y), snapped to whole art pixels.</summary>
    private void DrawGlyph(DrawingContext ctx, Fx fx, double x, double y, double scale, Point canvasOrigin)
    {
        var glyph = sprites.Effect(fx);
        var left = Math.Round(x - glyph.Source.Width / 2.0);
        var top = Math.Round(y - glyph.Source.Height / 2.0);
        SpriteLibrary.DrawAt(ctx, glyph, scale, new Point(canvasOrigin.X + left * scale, canvasOrigin.Y + top * scale));
    }
}
