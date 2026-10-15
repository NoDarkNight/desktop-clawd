using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DesktopClawd;

/// <summary>
/// One frame: a region of a sprite sheet (in art pixels), which of its pixels are opaque, its first
/// non-empty row (where the head is), its rightmost non-empty column, and the marker point (if any)
/// saying where a held object goes — or, in an object, where it's held. Several marker pixels mark their
/// centre, which can fall between pixels (two stacked markers = half a pixel lower than the top one).
/// </summary>
public readonly record struct SpriteFrame(
    Bitmap Sheet, PixelRect Source, bool[] Opaque, int TopRow, int RightCol, Point? Marker, bool MarkerBehind)
{
    public bool IsOpaque(int x, int y) =>
        x >= 0 && y >= 0 && x < Source.Width && y < Source.Height && Opaque[y * Source.Width + x];
}

/// <summary>
/// Loads, with built-in placeholders for anything missing:
/// animations from sprites/&lt;name&gt;.png (horizontal strips of square frames),
/// effects from sprites/fx/&lt;name&gt;.png (single images, never mirrored),
/// held objects from sprites/objects/&lt;name&gt;.png (single images, or &lt;name&gt;@N.png for N frames; mirrored with Clawd),
/// and emotes from sprites/emotes/&lt;name&gt;.png (strips of square frames, never mirrored).
/// </summary>
public sealed class SpriteLibrary
{
    private const double ObjectFps = 6;
    private static readonly int FrontMarker = (int)Color.Parse(SpriteFrames.FrontMarker).ToUInt32();
    private static readonly int BehindMarker = (int)Color.Parse(SpriteFrames.BehindMarker).ToUInt32();

    private Dictionary<Anim, SpriteFrame[]> _frames = new();
    private Dictionary<Anim, Anim> _artOf = new();
    private Dictionary<Fx, SpriteFrame> _fx = new();
    private Dictionary<string, SpriteFrame[]> _objects = new();
    private Dictionary<string, SpriteFrame[]> _emotes = new();
    private HashSet<string> _custom = [];

    public SpriteLibrary()
    {
        Directory = FindSpritesDirectory();
        Reload();
    }

    /// <summary>Where custom sprites are read from.</summary>
    public string Directory { get; }

    /// <summary>Canvas size in art pixels: large enough for every frame, and at least 16×16.</summary>
    public PixelSize Canvas { get; private set; }

    /// <summary>Art pixels to leave either side of the canvas so held objects and effects fit.</summary>
    public int SideMargin { get; private set; }

    public IReadOnlyList<EmoteInfo> Emotes { get; private set; } = [];

    public void Reload()
    {
        // Animations: custom strip, else placeholder, else borrow another animation's art.
        var frames = new Dictionary<Anim, SpriteFrame[]>();
        var customNames = new HashSet<string>();
        foreach (var anim in Enum.GetValues<Anim>())
        {
            if (anim == Anim.Emote) continue;
            var custom = TryLoadSheet(Path.Combine(Directory, SpriteFrames.FileName(anim)));
            if (custom is not null)
            {
                frames[anim] = CutSquares(custom);
                customNames.Add(anim.ToString());
            }
            else if (SpriteFrames.Placeholders.TryGetValue(anim, out var rows)) frames[anim] = rows.Select(r => Cut(FromText(r))).ToArray();
        }
        var artOf = new Dictionary<Anim, Anim>();
        foreach (var anim in Enum.GetValues<Anim>())
        {
            var source = anim;
            while (!frames.ContainsKey(source) && SpriteFrames.Fallbacks.TryGetValue(source, out var next)) source = next;
            artOf[anim] = frames.ContainsKey(source) ? source : Anim.Idle;
        }

        var fx = new Dictionary<Fx, SpriteFrame>();
        foreach (var effect in Enum.GetValues<Fx>())
        {
            // Variants (the small Z, balls 2 and 3) share their owner's file when there is one.
            var owner = SpriteFrames.FileOwner(effect);
            var custom = TryLoadSheet(Path.Combine(Directory, "fx", SpriteFrames.FileName(owner)!));
            if (custom is not null) customNames.Add("fx/" + owner);
            fx[effect] = Cut(custom ?? FromText(SpriteFrames.FxPlaceholders[effect], outline: true));
        }

        var objects = SpriteFrames.ObjectPlaceholders.ToDictionary(kv => kv.Key, kv => new[] { Cut(FromText(kv.Value)) });
        foreach (var path in ListPngs("objects"))
        {
            // "coffee@3.png" is a 3-frame strip named "coffee".
            var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^(?<name>.+?)(@(?<n>\d+))?$");
            if (TryLoadSheet(path) is not { } sheet) continue;
            var count = int.TryParse(match.Groups["n"].Value, out var n) && n > 0 && sheet.Width % n == 0 ? n : 1;
            objects[match.Groups["name"].Value.ToLowerInvariant()] = CutStrip(sheet, count, trim: true);
            customNames.Add("objects/" + match.Groups["name"].Value.ToLowerInvariant());
        }

        var emotes = new Dictionary<string, SpriteFrame[]>();
        foreach (var path in ListPngs("emotes"))
        {
            if (TryLoadSheet(path) is { } sheet)
                emotes[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = CutSquares(sheet);
        }

        var width = SpriteFrames.Canvas;
        var height = SpriteFrames.Canvas;
        foreach (var frame in frames.Values.Concat(emotes.Values).SelectMany(f => f))
        {
            width = Math.Max(width, frame.Source.Width);
            height = Math.Max(height, frame.Source.Height);
        }
        var widestObject = objects.Values.SelectMany(f => f).Select(f => f.Source.Width).DefaultIfEmpty(0).Max();

        // Old bitmaps aren't disposed: the renderer may still reference them, and reloads are rare.
        _frames = frames;
        _artOf = artOf;
        _fx = fx;
        _objects = objects;
        _emotes = emotes;
        _custom = customNames;
        Canvas = new PixelSize(width, height);
        SideMargin = Math.Max(8, widestObject + 2);
        // One performance plays the strip twice, and lasts at least two seconds.
        Emotes = emotes.Select(kv => new EmoteInfo(kv.Key, Math.Max(2, 2 * kv.Value.Length / SpriteFrames.EmoteFps))).ToList();
    }

    public SpriteFrame FrameAt(Anim anim, double seconds)
    {
        var art = _artOf[anim];
        var frames = _frames[art];
        return frames[(int)(seconds * SpriteFrames.Fps[art]) % frames.Length];
    }

    public SpriteFrame? EmoteFrameAt(string name, double seconds) =>
        _emotes.TryGetValue(name, out var frames) ? frames[(int)(seconds * SpriteFrames.EmoteFps) % frames.Length] : null;

    /// <summary>The object held during an animation (following fallbacks), if there is one.</summary>
    /// <remarks>
    /// An animation's own object wins if it exists (sip keeps its coffee while borrowing idle's frames);
    /// otherwise it uses the object of the animation whose art it borrows (reading shows the laptop until
    /// there's a book).
    /// </remarks>
    public SpriteFrame? HeldObjectAt(Anim anim, double seconds)
    {
        if (SpriteFrames.HeldObjects.TryGetValue(anim, out var own) && ObjectAt(own, seconds) is { } ownFrame)
            return ownFrame;
        return SpriteFrames.HeldObjects.TryGetValue(_artOf[anim], out var borrowed) ? ObjectAt(borrowed, seconds) : null;
    }

    /// <summary>A named object's frame, or null if there's no such object.</summary>
    public SpriteFrame? ObjectAt(string name, double seconds) =>
        _objects.TryGetValue(name, out var frames) ? frames[(int)(seconds * ObjectFps) % frames.Length] : null;

    public SpriteFrame Effect(Fx fx) => _fx[fx];

    /// <summary>Debug listing: where each sprite came from, its frames, and any marker pixel.</summary>
    public IEnumerable<string> Describe()
    {
        static string Marker(SpriteFrame[] f) => f.FirstOrDefault(x => x.Marker is not null) is { Marker: { } m } x
            ? $"  marker at {m.X:0.#},{m.Y:0.#} ({(x.MarkerBehind ? "behind" : "in front")})" : "";

        yield return $"Sprites folder: {Directory}";
        yield return $"Canvas {Canvas.Width}x{Canvas.Height}, side margin {SideMargin}";
        foreach (var anim in Enum.GetValues<Anim>())
        {
            if (anim == Anim.Emote) continue;
            var art = _artOf[anim];
            var source = art != anim ? $"uses {art}" : _custom.Contains(anim.ToString()) ? "your art" : "placeholder";
            var heldName = SpriteFrames.HeldObjects.TryGetValue(anim, out var own) && _objects.ContainsKey(own) ? own
                : SpriteFrames.HeldObjects.TryGetValue(art, out var borrowed) ? borrowed : null;
            var held = heldName is null ? "" : $"  holds {heldName}{(_objects.ContainsKey(heldName) ? "" : " (missing)")}";
            yield return $"  {anim,-10} {source,-15} {_frames[art].Length} frame(s){Marker(_frames[art])}{held}";
        }
        foreach (var (name, f) in _objects)
            yield return $"  objects/{name,-12} {(_custom.Contains("objects/" + name) ? "your art" : "placeholder"),-11} {f[0].Source.Width}x{f[0].Source.Height}, {f.Length} frame(s){Marker(f)}";
        foreach (var e in Emotes)
            yield return $"  emotes/{e.Name,-13} {_emotes[e.Name].Length} frame(s), {e.Duration:0.0}s per performance";
        foreach (var fx in Enum.GetValues<Fx>())
            if (SpriteFrames.FileName(fx) is not null)
                yield return $"  fx/{fx,-16} {(_custom.Contains("fx/" + fx) ? "your art" : "placeholder")}";
    }

    /// <summary>Top-left of a frame on the canvas, in art pixels (bottom-aligned, horizontally centred).</summary>
    public (int X, int Y) Placement(SpriteFrame frame) =>
        ((Canvas.Width - frame.Source.Width) / 2, Canvas.Height - frame.Source.Height);

    /// <summary>Draws a frame onto the canvas whose top-left is at <paramref name="origin"/>.</summary>
    public void Draw(DrawingContext ctx, SpriteFrame frame, double scale, bool flip, Point origin = default)
    {
        var (px, py) = Placement(frame);
        DrawOnCanvas(ctx, frame, px, py, scale, flip, origin);
    }

    /// <summary>
    /// Draws a held object at the character frame's marker pixel (or, without one, on the ground just
    /// in front of it), aligning the object's own marker — its grip — or else its bottom edge.
    /// </summary>
    public void DrawHeldObject(DrawingContext ctx, SpriteFrame character, SpriteFrame obj, double scale, bool flip, Point origin)
    {
        var (px, py) = Placement(character);
        var bottom = obj.Source.Height - 1;
        var (anchor, grip) = character.Marker is { } m
            ? (new Point(px + m.X, py + m.Y), obj.Marker ?? new Point(obj.Source.Width / 2, bottom))
            : (new Point(px + character.RightCol + 1, py + character.Source.Height - 1), obj.Marker ?? new Point(0, bottom));
        DrawOnCanvas(ctx, obj, anchor.X - grip.X, anchor.Y - grip.Y, scale, flip, origin);
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
    /// Writes every placeholder (animations as strips of 16×16 frames, effects under fx/, objects under
    /// objects/) in the formats custom sprites use, so they can be drawn over. Marker pixels are written
    /// in their marker colours. Returns the frame counts read back from the written files.
    /// </summary>
    public static IEnumerable<(string Name, string Path, int Frames)> ExportTemplates(string directory)
    {
        var size = SpriteFrames.Canvas;
        System.IO.Directory.CreateDirectory(directory);
        foreach (var (anim, frames) in SpriteFrames.Placeholders)
        {
            var path = Path.Combine(directory, SpriteFrames.FileName(anim));
            SavePng(path, size * frames.Length, size, (x, y) =>
            {
                // Frames side by side, each bottom-aligned in its 16×16 cell.
                var rows = frames[x / size];
                var row = y - (size - rows.Length);
                return row >= 0 && x % size < rows[row].Length ? rows[row][x % size] : '.';
            });
            yield return (anim.ToString(), path, TryLoadSheet(path) is { } s ? CutSquares(s).Length : 0);
        }

        foreach (var (folder, items) in new[]
        {
            ("fx", SpriteFrames.FxPlaceholders.Where(kv => SpriteFrames.FileName(kv.Key) is not null)
                .Select(kv => (Name: SpriteFrames.FileName(kv.Key)!, Rows: kv.Value))),
            ("objects", SpriteFrames.ObjectPlaceholders.Select(kv => (Name: kv.Key + ".png", Rows: kv.Value))),
        })
        {
            System.IO.Directory.CreateDirectory(Path.Combine(directory, folder));
            foreach (var (name, rows) in items)
            {
                var path = Path.Combine(directory, folder, name);
                SavePng(path, rows[0].Length, rows.Length, (x, y) => rows[y][x]);
                yield return ($"{folder}/{Path.GetFileNameWithoutExtension(name)}", path, TryLoadSheet(path) is null ? 0 : 1);
            }
        }
    }

    // --- Loading ---

    /// <summary>A decoded image: its bitmap (marker pixels removed), pixels as BGRA ints, and the markers found.</summary>
    private sealed record Sheet(Bitmap Bitmap, int Width, int Height, int[] Pixels, List<(int X, int Y, bool Behind)> Markers);

    private IEnumerable<string> ListPngs(string folder)
    {
        var dir = Path.Combine(Directory, folder);
        return System.IO.Directory.Exists(dir) ? System.IO.Directory.EnumerateFiles(dir, "*.png") : [];
    }

    private static Sheet? TryLoadSheet(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var decoded = new Bitmap(path);
            var (w, h) = (decoded.PixelSize.Width, decoded.PixelSize.Height);
            var bitmap = new WriteableBitmap(decoded.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var pixels = new int[w * h];
            var markers = new List<(int, int, bool)>();
            using (var buffer = bitmap.Lock())
            {
                decoded.CopyPixels(buffer);
                for (var y = 0; y < h; y++)
                    Marshal.Copy(buffer.Address + y * buffer.RowBytes, pixels, y * w, w);

                // Marker pixels are instructions, not art: note them and erase them.
                for (var i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i] != FrontMarker && pixels[i] != BehindMarker) continue;
                    markers.Add((i % w, i / w, pixels[i] == BehindMarker));
                    pixels[i] = 0;
                }
                for (var y = 0; y < h; y++)
                    Marshal.Copy(pixels, y * w, buffer.Address + y * buffer.RowBytes, w);
            }
            return new Sheet(bitmap, w, h, pixels, markers);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not load sprite {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Builds a sheet from text rows, optionally adding a 1-pixel dark outline around the art.</summary>
    private static Sheet FromText(string[] rows, bool outline = false)
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
        var markers = new List<(int, int, bool)>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var c = At(x, y);
            if (c is 'M' or 'C') markers.Add((x, y, c == 'C'));
            else if (SpriteFrames.Palette.TryGetValue(c, out var hex)) pixels[y * width + x] = (int)Color.Parse(hex).ToUInt32();
            else if (outline && (Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1)))
                pixels[y * width + x] = outlineColor;
        }

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width, buffer.Address + y * buffer.RowBytes, width);
        }
        return new Sheet(bitmap, width, height, pixels, markers);
    }

    /// <summary>Square frames side by side; anything else is treated as a single frame.</summary>
    private static SpriteFrame[] CutSquares(Sheet sheet) =>
        CutStrip(sheet, sheet.Width % sheet.Height == 0 ? sheet.Width / sheet.Height : 1);

    /// <param name="trim">
    /// Crop away empty space around the art (and markers), using one box for every frame so
    /// animated frames stay aligned. Used for objects, whose default grip is their bottom edge.
    /// </param>
    private static SpriteFrame[] CutStrip(Sheet sheet, int count, bool trim = false)
    {
        var frameWidth = sheet.Width / count;
        var rects = Enumerable.Range(0, count).Select(i => new PixelRect(i * frameWidth, 0, frameWidth, sheet.Height)).ToArray();

        if (trim)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            foreach (var r in rects)
            for (var y = 0; y < r.Height; y++)
            for (var x = 0; x < r.Width; x++)
            {
                var filled = (uint)sheet.Pixels[y * sheet.Width + r.X + x] >> 24 >= 128
                             || sheet.Markers.Any(m => m.X == r.X + x && m.Y == y);
                if (!filled) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            if (maxX >= 0)
                rects = rects.Select(r => new PixelRect(r.X + minX, minY, maxX - minX + 1, maxY - minY + 1)).ToArray();
        }
        return rects.Select(r => Cut(sheet, r)).ToArray();
    }

    private static SpriteFrame Cut(Sheet sheet) => Cut(sheet, new PixelRect(0, 0, sheet.Width, sheet.Height));

    private static SpriteFrame Cut(Sheet sheet, PixelRect source)
    {
        var opaque = new bool[source.Width * source.Height];
        int top = source.Height, right = -1;
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var alpha = (uint)sheet.Pixels[(source.Y + y) * sheet.Width + source.X + x] >> 24;
            if (alpha < 128) continue;
            opaque[y * source.Width + x] = true;
            top = Math.Min(top, y);
            right = Math.Max(right, x);
        }

        // Several marker pixels mark their centre; front/behind comes from the first one.
        var inFrame = sheet.Markers.Where(m => source.Contains(new PixelPoint(m.X, m.Y))).ToList();
        Point? marker = inFrame.Count == 0 ? null
            : new Point(inFrame.Average(m => m.X) - source.X, inFrame.Average(m => m.Y) - source.Y);
        var behind = inFrame.Count > 0 && inFrame[0].Behind;
        return new SpriteFrame(sheet.Bitmap, source, opaque, top == source.Height ? 0 : top, Math.Max(right, 0), marker, behind);
    }

    // --- Drawing and saving ---

    private void DrawOnCanvas(DrawingContext ctx, SpriteFrame frame, double x, double y, double scale, bool flip, Point origin)
    {
        var dest = new Rect(origin.X + x * scale, origin.Y + y * scale, frame.Source.Width * scale, frame.Source.Height * scale);
        // Mirror around the canvas centre when facing left.
        var transform = flip
            ? Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(2 * origin.X + Canvas.Width * scale, 0)
            : Matrix.Identity;
        using (ctx.PushTransform(transform))
        {
            DrawRegion(ctx, frame, dest);
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
                {
                    line[x] = pixelAt(x, y) switch
                    {
                        'M' => FrontMarker,
                        'C' => BehindMarker,
                        var c => SpriteFrames.Palette.TryGetValue(c, out var hex) ? (int)Color.Parse(hex).ToUInt32() : 0,
                    };
                }
                Marshal.Copy(line, 0, buffer.Address + y * buffer.RowBytes, width);
            }
        }
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// Prefers the project's own sprites folder when running from a build under it,
    /// so "Reload sprites" picks up edits without rebuilding; otherwise uses the one next to the exe.
    /// </summary>
    private static string FindSpritesDirectory()
    {
        if (Environment.GetEnvironmentVariable("CLAWD_SPRITES") is { Length: > 0 } custom) return Path.GetFullPath(custom);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DesktopClawd.csproj")))
                return Path.Combine(dir.FullName, "sprites");
        }
        return Path.Combine(AppContext.BaseDirectory, "sprites");
    }
}
