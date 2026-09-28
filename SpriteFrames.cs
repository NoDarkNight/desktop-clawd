using System.Collections.Generic;

namespace DesktopClawd;

/// <summary>Every animation the pet can show. Custom art goes in sprites/&lt;name&gt;.png (lowercase).</summary>
public enum Anim { Idle, Blink, Walk, Sleep, Held, Fall, Land, Happy, Working, Alert, Celebrate }

/// <summary>
/// Built-in placeholder art, used for any animation without a PNG in the sprites folder.
/// Frames face right and are bottom-aligned on the canvas; renderers mirror them for left.
/// '.' = transparent, 'O' = body, 'D' = body shade, 'E' = eyes, 'Y' = accent, 'W' = white.
/// </summary>
public static class SpriteFrames
{
    /// <summary>Minimum canvas size in art pixels. Custom sprites may be larger.</summary>
    public const int Canvas = 16;

    public static readonly IReadOnlyDictionary<char, string> Palette = new Dictionary<char, string>
    {
        ['O'] = "#D97757",
        ['D'] = "#B35B3D",
        ['E'] = "#1F1E1D",
        ['Y'] = "#F2C94C",
        ['W'] = "#F4F1EC",
    };

    /// <summary>Playback speed per animation, in frames per second.</summary>
    public static readonly IReadOnlyDictionary<Anim, double> Fps = new Dictionary<Anim, double>
    {
        [Anim.Idle] = 2,
        [Anim.Blink] = 1,
        [Anim.Walk] = 8,
        [Anim.Sleep] = 1.5,
        [Anim.Held] = 4,
        [Anim.Fall] = 4,
        [Anim.Land] = 1,
        [Anim.Happy] = 6,
        [Anim.Working] = 6,
        [Anim.Alert] = 3,
        [Anim.Celebrate] = 6,
    };

    public static string FileName(Anim anim) => anim.ToString().ToLowerInvariant() + ".png";

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

    private static readonly string[] SleepA =
    [
        "..........WWW...",
        "...........W....",
        "..........WWW...",
        "................",
        "................",
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
        "............WWW.",
        ".............W..",
        "............WWW.",
        "................",
        "................",
        "................",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "OOOOOEEOOOOEEOOO",
        "OOOOOOOOOOOOOOOO",
        "..OOOOOOOOOOOO..",
        "..OOOOOOOOOOOO..",
        "..DDDDDDDDDDDD..",
        "...O.O....O.O...",
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
        "...O.O....O.O...",
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
        "...O.O....O.O...",
    ];

    // "!" above, waving one arm.
    private static readonly string[] AlertA =
    [
        ".......YY.......",
        ".......YY.......",
        ".......YY.......",
        "................",
        ".......YY.......",
        "................",
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

    private static readonly string[] AlertB =
    [
        ".......YY.......",
        ".......YY.......",
        ".......YY.......",
        "................",
        ".......YY.......",
        "................",
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

    // Arms up with sparkles.
    private static readonly string[] CelebrateB =
    [
        ".Y............Y.",
        "................",
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

    // Declared last: static fields initialise in order, so the frames above must exist first.
    public static readonly IReadOnlyDictionary<Anim, string[][]> Placeholders = new Dictionary<Anim, string[][]>
    {
        [Anim.Idle] = [Idle],
        [Anim.Blink] = [Blink],
        [Anim.Walk] = [WalkA, WalkB],
        [Anim.Sleep] = [SleepA, SleepB],
        [Anim.Held] = [WalkA],
        [Anim.Fall] = [WalkA],
        [Anim.Land] = [Land],
        [Anim.Happy] = [Happy],
        [Anim.Working] = [WorkingA, WorkingB],
        [Anim.Alert] = [AlertA, AlertB],
        [Anim.Celebrate] = [Happy, CelebrateB],
    };
}
