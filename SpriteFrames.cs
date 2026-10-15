using System.Collections.Generic;

namespace DesktopClawd;

/// <summary>Every animation the pet can show. Custom art goes in sprites/&lt;name&gt;.png (lowercase).</summary>
public enum Anim { Idle, Blink, Walk, Sleep, Held, Fall, Jump, Land, Slide, Dizzy, Wake, Happy, Sip, Working, Reading, Searching, Thinking, Alert, Celebrate, Emote }

/// <summary>
/// Effect glyphs drawn around the character and never mirrored, so text-like shapes stay readable.
/// Custom art goes in sprites/fx/&lt;name&gt;.png (a single image each).
/// </summary>
public enum Fx { Z, ZSmall, Alert, Sparkle, Star }

/// <summary>
/// Built-in placeholder art, used for any animation or effect without a PNG in the sprites folder.
/// Character frames face right and are bottom-aligned on the canvas; renderers mirror them for left.
/// '.' = transparent, 'O' = body, 'D' = body shade, 'E' = eyes, 'Y' = accent, 'W' = white,
/// 'G'/'K'/'B' = object greys and screen blue. Marker pixels (never drawn): 'M' = held object goes
/// here in front of the character, 'C' = behind it; in an object, 'M' is the point it's held by.
/// </summary>
public static class SpriteFrames
{
    /// <summary>Minimum canvas size in art pixels. Custom sprites may be larger.</summary>
    public const int Canvas = 16;

    /// <summary>Colour of the outline added around placeholder effect glyphs, so they read on any background.</summary>
    public const string FxOutline = "#1F1E1D";

    public static readonly IReadOnlyDictionary<char, string> Palette = new Dictionary<char, string>
    {
        ['O'] = "#D97757",
        ['D'] = "#B35B3D",
        ['E'] = "#1F1E1D",
        ['Y'] = "#F2C94C",
        ['W'] = "#F4F1EC",
        ['G'] = "#9C9A96",
        ['K'] = "#5A5856",
        ['B'] = "#8EC9FF",
    };

    /// <summary>Marker colours in custom PNGs, matching the 'M' and 'C' placeholder characters.</summary>
    public const string FrontMarker = "#FF00FF";
    public const string BehindMarker = "#00FFFF";

    /// <summary>Animations with no art of their own borrow another's frames (and held object).</summary>
    public static readonly IReadOnlyDictionary<Anim, Anim> Fallbacks = new Dictionary<Anim, Anim>
    {
        [Anim.Slide] = Anim.Idle,
        [Anim.Sip] = Anim.Idle,
        [Anim.Reading] = Anim.Working,
        [Anim.Searching] = Anim.Working,
        [Anim.Thinking] = Anim.Working,
    };

    /// <summary>Object (sprites/objects/&lt;name&gt;.png) held during an animation, if any.</summary>
    public static readonly IReadOnlyDictionary<Anim, string> HeldObjects = new Dictionary<Anim, string>
    {
        [Anim.Sip] = "coffee",
        [Anim.Working] = "laptop",
        [Anim.Reading] = "book",
        [Anim.Searching] = "binoculars",
    };

    /// <summary>Playback speed of emotes, in frames per second.</summary>
    public const double EmoteFps = 6;

    /// <summary>Playback speed per animation, in frames per second.</summary>
    public static readonly IReadOnlyDictionary<Anim, double> Fps = new Dictionary<Anim, double>
    {
        [Anim.Idle] = 2,
        [Anim.Blink] = 1,
        [Anim.Walk] = 8,
        [Anim.Sleep] = 0.8,
        [Anim.Held] = 4,
        [Anim.Fall] = 4,
        [Anim.Jump] = 4,
        [Anim.Land] = 1,
        [Anim.Slide] = 4,
        [Anim.Dizzy] = 4,
        [Anim.Wake] = 2,
        [Anim.Happy] = 6,
        [Anim.Sip] = 3,
        [Anim.Working] = 6,
        [Anim.Reading] = 3,
        [Anim.Searching] = 3,
        [Anim.Thinking] = 3,
        [Anim.Alert] = 3,
        [Anim.Celebrate] = 6,
        [Anim.Emote] = EmoteFps,
    };

    public static string FileName(Anim anim) => anim.ToString().ToLowerInvariant() + ".png";

    /// <summary>File for an effect glyph, or null if it has no file of its own.</summary>
    public static string? FileName(Fx fx) => fx switch
    {
        Fx.ZSmall => null, // uses z.png when that exists
        _ => fx.ToString().ToLowerInvariant() + ".png",
    };

    private static readonly string[] Idle =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "...O.O....O.O...",
    ];

    private static readonly string[] Blink =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "...O.O....O.O...",
    ];

    private static readonly string[] WalkA =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "..O...O..O...O..",
    ];

    private static readonly string[] WalkB =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "....OO....OO....",
        "....OO....OO....",
    ];

    private static readonly string[] Happy =
    [
        "OOOOOOOOOOOOOOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "..O...O..O...O..",
    ];

    // Arms up, legs tucked.
    private static readonly string[] ArmsUpTucked =
    [
        "OOOOOOOOOOOOOOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "....OO....OO....",
        "....OO....OO....",
    ];

    private static readonly string[] Land =
    [
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "..O..O....O..O..",
    ];

    // Eyes closed, gently breathing.
    private static readonly string[] SleepA =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
    ];

    private static readonly string[] SleepB =
    [
        "..OOOOOOOOOOOO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
    ];

    // Knocked flat, eyes wobbling at different heights, legs splayed.
    private static readonly string[] DizzyA =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOEEOOOOOOOOO",
        "..DDDDDDDDDDDD..",
        ".O..O......O..O.",
    ];

    private static readonly string[] DizzyB =
    [
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOOOO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOEEOOO",
        "..DDDDDDDDDDDD..",
        ".O..O......O..O.",
    ];

    // Looking down, arms alternating as if typing.
    private static readonly string[] WorkingA =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "OOOOOEEOOOOEEO..",
        "OOOOOEEOOOOEEOOO",
        "..OOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "...O.O....O.O..M",
    ];

    private static readonly string[] WorkingB =
    [
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOEEOOOOEEOOO",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "...O.O....O.O..M",
    ];

    // Waving one arm (the "!" is an effect).
    private static readonly string[] Wave =
    [
        "..OOOOOOOOOOOOOO",
        "..OOOOOOOOOOOOOO",
        "..OOOEEOOOOEEO..",
        "OOOOOEEOOOOEEO..",
        "OOOOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
        "...O.O....O.O...",
    ];

    // Declared after the frames: static fields initialise in order.
    public static readonly IReadOnlyDictionary<Anim, string[][]> Placeholders = new Dictionary<Anim, string[][]>
    {
        [Anim.Idle] = [Idle],
        [Anim.Blink] = [Blink],
        [Anim.Walk] = [WalkA, WalkB],
        [Anim.Sleep] = [SleepA, SleepB],
        [Anim.Held] = [WalkA],
        [Anim.Fall] = [WalkA],
        [Anim.Jump] = [ArmsUpTucked],
        [Anim.Land] = [Land],
        [Anim.Dizzy] = [DizzyA, DizzyB],
        [Anim.Wake] = [Blink, Happy],
        [Anim.Happy] = [Happy],
        [Anim.Working] = [WorkingA, WorkingB],
        [Anim.Alert] = [Wave, Idle],
        [Anim.Celebrate] = [Happy, ArmsUpTucked],
    };

    /// <summary>Built-in objects. 'M' marks where the object is held (bottom-left of the laptop's base).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ObjectPlaceholders = new Dictionary<string, string[]>
    {
        // Side view of an open laptop, screen facing back towards the character.
        ["laptop"] =
        [
            ".......G",
            "......BG",
            "......BG",
            ".....BG.",
            ".....BG.",
            "GGGGGGGG",
            "MKKKKKKK",
        ],
        // A mug, handle towards the character.
        ["coffee"] =
        [
            "..WWW",
            "WWWWW",
            "W.WWW",
            "WWWWW",
            "..WWW",
        ],
    };

    public static readonly IReadOnlyDictionary<Fx, string[]> FxPlaceholders = new Dictionary<Fx, string[]>
    {
        // A Z needs at least 5 rows so the diagonal doesn't collapse into an "I".
        [Fx.Z] =
        [
            "WWWWWW",
            "....WW",
            "...WW.",
            "..WW..",
            ".WW...",
            "WWWWWW",
        ],
        [Fx.ZSmall] =
        [
            "WWWWW",
            "...W.",
            "..W..",
            ".W...",
            "WWWWW",
        ],
        [Fx.Alert] =
        [
            "YY",
            "YY",
            "YY",
            "YY",
            "..",
            "YY",
        ],
        [Fx.Sparkle] =
        [
            ".Y.",
            "YWY",
            ".Y.",
        ],
        [Fx.Star] =
        [
            "..Y..",
            "YYYYY",
            ".YYY.",
            ".Y.Y.",
        ],
    };
}
