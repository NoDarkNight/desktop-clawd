using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DesktopClawd;

/// <summary>
/// One frame: a region of a sprite sheet (in art pixels), which of its pixels are opaque,
/// and its first non-empty row (where the head is).
/// </summary>
public readonly record struct SpriteFrame(Bitmap Sheet, PixelRect Source, bool[] Opaque, int TopRow)
{
    public bool IsOpaque(int x, int y) =>
        x >= 0 && y >= 0 && x < Source.Width && y < Source.Height && Opaque[y * Source.Width + x];
}

/// <summary>
/// Loads each animation from sprites/&lt;name&gt;.png (a horizontal strip of square frames) and each
/// effect from sprites/fx/&lt;name&gt;.png, falling back to the built-in placeholders in <see cref="SpriteFrames"/>.
/// </summary>
public sealed class SpriteLibrary
{
    private Dictionary<Anim, SpriteFrame[]> _frames = new();
    private Dictionary<Fx, SpriteFrame> _fx = new();

    public SpriteLibrary()
    {
        Directory = FindSpritesDirectory();
        Reload();
    }

    /// <summary>Where custom sprites are read from.</summary>
    public string Directory { get; }

    /// <summary>Canvas size in art pixels: large enough for every frame, and at least 16×16.</summary>
    public PixelSize Canvas { get; private set; }

    public void Reload()
    {
        var frames = new Dictionary<Anim, SpriteFrame[]>();
        foreach (var anim in Enum.GetValues<Anim>())
        {
            frames[anim] = TryLoadStrip(Path.Combine(Directory, SpriteFrames.FileName(anim)))
                           ?? SpriteFrames.Placeholders[anim].Select(rows => FromText(rows)).ToArray();
        }

        var fx = new Dictionary<Fx, SpriteFrame>();
        var customZ = TryLoadImage(Path.Combine(Directory, "fx", SpriteFrames.FileName(Fx.Z)!));
        foreach (var effect in Enum.GetValues<Fx>())
        {
            var custom = effect == Fx.ZSmall
                ? customZ
                : TryLoadImage(Path.Combine(Directory, "fx", SpriteFrames.FileName(effect)!));
            fx[effect] = custom ?? FromText(SpriteFrames.FxPlaceholders[effect], outline: true);
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
        _fx = fx;
        Canvas = new PixelSize(width, height);
    }

    public SpriteFrame FrameAt(Anim anim, double seconds)
    {
        var frames = _frames[anim];
        var index = (int)(seconds * SpriteFrames.Fps[anim]) % frames.Length;
        return frames[index];
    }

    public SpriteFrame Effect(Fx fx) => _fx[fx];

    /// <summary>Top-left of a frame on the canvas, in art pixels (bottom-aligned, horizontally centred).</summary>
    public (int X, int Y) Placement(SpriteFrame frame) =>
        ((Canvas.Width - frame.Source.Width) / 2, Canvas.Height - frame.Source.Height);

    /// <summary>Draws a frame onto the canvas whose top-left is at <paramref name="origin"/>.</summary>
    public void Draw(DrawingContext ctx, SpriteFrame frame, double scale, bool flip, Point origin = default)
    {
        var (px, py) = Placement(frame);
        var dest = new Rect(origin.X + px * scale, origin.Y + py * scale, frame.Source.Width * scale, frame.Source.Height * scale);

        // Mirror around the canvas centre when facing left.
        var transform = flip
            ? Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(2 * origin.X + Canvas.Width * scale, 0)
            : Matrix.Identity;
        using (ctx.PushTransform(transform))
        {
            DrawRegion(ctx, frame, dest);
        }
    }

    /// <summary>Draws a frame unmirrored with its top-left at the given point.</summary>
    public static void DrawAt(DrawingContext ctx, SpriteFrame frame, double scale, Point topLeft) =>
        DrawRegion(ctx, frame, new Rect(topLeft.X, topLeft.Y, frame.Source.Width * scale, frame.Source.Height * scale));

    /// <summary>Whether the art pixel at canvas coordinates (x, y) is opaque in the frame as drawn.</summary>
    public bool HitTest(SpriteFrame frame, bool flip, double x, double y)
    {
        if (flip) x = Canvas.Width - x;
        var (px, py) = Placement(frame);
        return frame.IsOpaque((int)Math.Floor(x - px), (int)Math.Floor(y - py));
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
    /// Writes each placeholder animation as a PNG strip of 16×16 frames, and each effect glyph
    /// under fx/, in the same formats custom sprites use, so they can be drawn over.
    /// Returns the frame counts read back from the written files.
    /// </summary>
    public static IEnumerable<(string Name, string Path, int Frames)> ExportTemplates(string directory)
    {
        var size = SpriteFrames.Canvas;
        System.IO.Directory.CreateDirectory(directory);
        foreach (var anim in Enum.GetValues<Anim>())
        {
            var frames = SpriteFrames.Placeholders[anim];
            var path = Path.Combine(directory, SpriteFrames.FileName(anim));
            SavePng(path, size * frames.Length, size, (x, y) =>
            {
                // Frames side by side, each bottom-aligned in its 16×16 cell.
                var rows = frames[x / size];
                var row = y - (size - rows.Length);
                return row >= 0 && x % size < rows[row].Length ? rows[row][x % size] : '.';
            });
            yield return (anim.ToString(), path, TryLoadStrip(path)?.Length ?? 0);
        }

        var fxDir = Path.Combine(directory, "fx");
        System.IO.Directory.CreateDirectory(fxDir);
        foreach (var fx in Enum.GetValues<Fx>())
        {
            if (SpriteFrames.FileName(fx) is not { } name) continue;
            var rows = SpriteFrames.FxPlaceholders[fx];
            var path = Path.Combine(fxDir, name);
            SavePng(path, rows[0].Length, rows.Length, (x, y) => rows[y][x]);
            yield return ($"fx/{fx}", path, TryLoadImage(path) is null ? 0 : 1);
        }
    }

    private static void DrawRegion(DrawingContext ctx, SpriteFrame frame, Rect dest)
    {
        var src = frame.Source;
        ctx.DrawImage(frame.Sheet, new Rect(src.X, src.Y, src.Width, src.Height), dest);
    }

    private static void SavePng(string path, int width, int height, Func<int, int, char> pixelAt)
    {
        var size = new PixelSize(width, height);
        using var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var line = new int[width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    line[x] = SpriteFrames.Palette.TryGetValue(pixelAt(x, y), out var hex) ? (int)Color.Parse(hex).ToUInt32() : 0;
                Marshal.Copy(line, 0, buffer.Address + y * buffer.RowBytes, width);
            }
        }
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static SpriteFrame[]? TryLoadStrip(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var sheet = new Bitmap(path);
            var (w, h) = (sheet.PixelSize.Width, sheet.PixelSize.Height);
            var alpha = ReadAlpha(sheet);
            // Square frames side by side; anything else is treated as a single frame.
            var count = w % h == 0 ? w / h : 1;
            var frameWidth = w / count;
            return Enumerable.Range(0, count)
                .Select(i => MakeFrame(sheet, new PixelRect(i * frameWidth, 0, frameWidth, h), alpha, w))
                .ToArray();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not load sprite {path}: {ex.Message}");
            return null;
        }
    }

    private static SpriteFrame? TryLoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var sheet = new Bitmap(path);
            return MakeFrame(sheet, new PixelRect(sheet.PixelSize), ReadAlpha(sheet), sheet.PixelSize.Width);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not load effect {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Alpha channel of a whole sheet, row-major.</summary>
    private static byte[] ReadAlpha(Bitmap sheet)
    {
        var size = sheet.PixelSize;
        using var copy = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = copy.Lock();
        sheet.CopyPixels(buffer);
        var alpha = new byte[size.Width * size.Height];
        var row = new byte[size.Width * 4];
        for (var y = 0; y < size.Height; y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
            for (var x = 0; x < size.Width; x++) alpha[y * size.Width + x] = row[x * 4 + 3];
        }
        return alpha;
    }

    private static SpriteFrame MakeFrame(Bitmap sheet, PixelRect source, byte[] sheetAlpha, int sheetWidth)
    {
        var opaque = new bool[source.Width * source.Height];
        var top = source.Height;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            if (sheetAlpha[(source.Y + y) * sheetWidth + source.X + x] < 128) continue;
            opaque[y * source.Width + x] = true;
            top = Math.Min(top, y);
        }
        return new SpriteFrame(sheet, source, opaque, top == source.Height ? 0 : top);
    }

    /// <summary>Builds a frame from text rows, optionally adding a 1-pixel dark outline around it.</summary>
    private static SpriteFrame FromText(string[] rows, bool outline = false)
    {
        var pad = outline ? 1 : 0;
        var width = rows.Max(r => r.Length) + 2 * pad;
        var height = rows.Length + 2 * pad;

        char At(int x, int y)
        {
            x -= pad;
            y -= pad;
            return y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : '.';
        }
        bool Filled(int x, int y) => SpriteFrames.Palette.ContainsKey(At(x, y));

        var outlineColor = (int)Color.Parse(SpriteFrames.FxOutline).ToUInt32();
        var pixels = new int[width * height];
        var alpha = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = y * width + x;
            if (SpriteFrames.Palette.TryGetValue(At(x, y), out var hex))
                pixels[i] = (int)Color.Parse(hex).ToUInt32();
            else if (outline && (Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1)))
                pixels[i] = outlineColor;
            if (pixels[i] != 0) alpha[i] = 255;
        }

        var size = new PixelSize(width, height);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width, buffer.Address + y * buffer.RowBytes, width);
        }
        return MakeFrame(bitmap, new PixelRect(size), alpha, width);
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
