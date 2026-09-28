using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DesktopClawd;

/// <summary>One frame: a region of a sprite sheet, in art pixels.</summary>
public readonly record struct SpriteFrame(Bitmap Sheet, PixelRect Source);

/// <summary>
/// Loads each animation from sprites/&lt;name&gt;.png (a horizontal strip of square frames),
/// falling back to the built-in placeholders in <see cref="SpriteFrames"/>.
/// </summary>
public sealed class SpriteLibrary
{
    private Dictionary<Anim, SpriteFrame[]> _frames = new();

    public SpriteLibrary()
    {
        Directory = FindSpritesDirectory();
        Reload();
    }

    /// <summary>Where custom sprites are read from.</summary>
    public string Directory { get; }

    /// <summary>Animations currently using custom PNGs rather than placeholders.</summary>
    public IReadOnlyList<Anim> Custom { get; private set; } = [];

    /// <summary>Canvas size in art pixels: large enough for every frame, and at least 16×16.</summary>
    public PixelSize Canvas { get; private set; }

    public void Reload()
    {
        var frames = new Dictionary<Anim, SpriteFrame[]>();
        var custom = new List<Anim>();
        foreach (var anim in Enum.GetValues<Anim>())
        {
            var loaded = TryLoadStrip(Path.Combine(Directory, SpriteFrames.FileName(anim)));
            if (loaded is not null) custom.Add(anim);
            frames[anim] = loaded ?? SpriteFrames.Placeholders[anim].Select(FromText).ToArray();
        }

        var width = SpriteFrames.Canvas;
        var height = SpriteFrames.Canvas;
        foreach (var frame in frames.Values.SelectMany(f => f))
        {
            width = Math.Max(width, frame.Source.Width);
            height = Math.Max(height, frame.Source.Height);
        }

        // Old bitmaps aren't disposed: the renderer may still reference them, and reloads are rare.
        _frames = frames;
        Custom = custom;
        Canvas = new PixelSize(width, height);
    }

    public SpriteFrame FrameAt(Anim anim, double seconds)
    {
        var frames = _frames[anim];
        var index = (int)(seconds * SpriteFrames.Fps[anim]) % frames.Length;
        return frames[index];
    }

    /// <summary>Draws a frame bottom-aligned and horizontally centred on the canvas.</summary>
    public void Draw(DrawingContext ctx, SpriteFrame frame, double scale, bool flip, Point offset = default)
    {
        var src = frame.Source;
        var x = offset.X + (Canvas.Width - src.Width) / 2 * scale;
        var y = offset.Y + (Canvas.Height - src.Height) * scale;
        var dest = new Rect(x, y, src.Width * scale, src.Height * scale);

        // Mirror around the canvas centre when facing left.
        var transform = flip
            ? Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(2 * offset.X + Canvas.Width * scale, 0)
            : Matrix.Identity;
        using (ctx.PushTransform(transform))
        {
            ctx.DrawImage(frame.Sheet, new Rect(src.X, src.Y, src.Width, src.Height), dest);
        }
    }

    public WindowIcon CreateIcon()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
        var scale = Math.Max(1, 32 / Math.Max(Canvas.Width, Canvas.Height));
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            var offset = new Point((32 - Canvas.Width * scale) / 2.0, (32 - Canvas.Height * scale) / 2.0);
            Draw(ctx, FrameAt(Anim.Idle, 0), scale, flip: false, offset);
        }
        return new WindowIcon(bitmap);
    }

    /// <summary>
    /// Writes each placeholder animation as a PNG strip of 16×16 frames, the same format custom
    /// sprites use, so they can be opened in an editor and drawn over. Returns the frame counts
    /// read back from the written files.
    /// </summary>
    public static IEnumerable<(Anim Anim, string Path, int Frames)> ExportTemplates(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        var size = SpriteFrames.Canvas;
        foreach (var anim in Enum.GetValues<Anim>())
        {
            var frames = SpriteFrames.Placeholders[anim];
            var strip = new PixelSize(size * frames.Length, size);
            using var bitmap = new WriteableBitmap(strip, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
            {
                var pixels = new int[strip.Width * strip.Height];
                for (var f = 0; f < frames.Length; f++)
                {
                    var rows = frames[f];
                    var top = size - rows.Length; // bottom-aligned
                    for (var y = 0; y < rows.Length; y++)
                    for (var x = 0; x < rows[y].Length; x++)
                    {
                        if (SpriteFrames.Palette.TryGetValue(rows[y][x], out var hex))
                            pixels[(top + y) * strip.Width + f * size + x] = (int)Color.Parse(hex).ToUInt32();
                    }
                }
                for (var y = 0; y < strip.Height; y++)
                    Marshal.Copy(pixels, y * strip.Width, buffer.Address + y * buffer.RowBytes, strip.Width);
            }

            var path = Path.Combine(directory, SpriteFrames.FileName(anim));
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
            yield return (anim, path, TryLoadStrip(path)?.Length ?? 0);
        }
    }

    private static SpriteFrame[]? TryLoadStrip(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var sheet = new Bitmap(path);
            var (w, h) = (sheet.PixelSize.Width, sheet.PixelSize.Height);
            // Square frames side by side; anything else is treated as a single frame.
            var count = w % h == 0 ? w / h : 1;
            var frameWidth = w / count;
            return Enumerable.Range(0, count)
                .Select(i => new SpriteFrame(sheet, new PixelRect(i * frameWidth, 0, frameWidth, h)))
                .ToArray();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not load sprite {path}: {ex.Message}");
            return null;
        }
    }

    private static SpriteFrame FromText(string[] rows)
    {
        var size = new PixelSize(rows.Max(r => r.Length), rows.Length);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var line = new int[size.Width];
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    var c = x < rows[y].Length ? rows[y][x] : '.';
                    line[x] = SpriteFrames.Palette.TryGetValue(c, out var hex) ? (int)Color.Parse(hex).ToUInt32() : 0;
                }
                Marshal.Copy(line, 0, buffer.Address + y * buffer.RowBytes, size.Width);
            }
        }
        return new SpriteFrame(bitmap, new PixelRect(size));
    }

    /// <summary>
    /// Prefers the project's own sprites folder when running from a build under it,
    /// so "Reload sprites" picks up edits without rebuilding; otherwise uses the one next to the exe.
    /// </summary>
    private static string FindSpritesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DesktopClawd.csproj")))
                return Path.Combine(dir.FullName, "sprites");
        }
        return Path.Combine(AppContext.BaseDirectory, "sprites");
    }
}
