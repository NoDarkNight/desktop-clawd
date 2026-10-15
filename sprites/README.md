# Sprites

Put your PNGs in this folder. Anything without a PNG uses a built-in placeholder, so you can
replace things one at a time. After saving, right-click the tray icon → **Reload sprites**
(no rebuild needed). To see what loaded and from where: `dotnet run -- --list-sprites`.

`templates/` has the current placeholders in the right formats — open one in Piskel and draw over it.
Regenerate them with `dotnet run -- --export-templates sprites/templates`.

| Folder | Holds | Mirrored when Clawd faces left? |
|---|---|---|
| `sprites/` | Clawd's animations | yes |
| `sprites/objects/` | Things Clawd holds (laptop, coffee, ball…) | yes, with Clawd |
| `sprites/fx/` | Effects around Clawd (Zs, "!", sparkles, stars) | **no** |
| `sprites/emotes/` | Extra animations Clawd plays now and then while idle | **no** |

## Character animations (`sprites/`)

Draw **only Clawd** — no Zs, "!" or props.

- **PNG, transparent background.** One art pixel = one image pixel (don't upscale; the pet does that).
- **16×16 per frame**, character facing **right**, feet on the **bottom row**. Frames may be larger
  if needed (the window grows to fit), but all frames in one file must be the same size.
- **Frames side by side in one horizontal strip**, e.g. a 4-frame walk is 64×16.
  In Piskel: *Export → PNG → Spritesheet*, 1 row.
- **No anti-aliasing / soft brushes** — every pixel fully opaque or fully transparent.
  Only opaque pixels catch the mouse; clicks on transparent pixels go to whatever is behind.

| File | Frames | When it plays | Holds | Speed (fps) |
|---|---|---|---|---|
| `idle.png` | 1–4 | Standing around | | 2 |
| `blink.png` | 1 | Eyes closed (flashed briefly while idle) | | – |
| `walk.png` | 2–6 | Walking | | 8 |
| `sleep.png` | 1–6 | Napping while you're away | | 0.8 |
| `wake.png` | 2 | Stretching as it wakes up | | 2 |
| `held.png` | 1–2 | Being dragged | | 4 |
| `fall.png` | 1 | Falling / thrown | | 4 |
| `jump.png` | 1–2 | Leaping up onto a window | | 4 |
| `land.png` | 1 | Squash on landing (~0.1 s) and on each bounce | | – |
| `slide.png` | 1–2 | Skidding to a stop after a throw *(uses `land.png` until drawn)* | | 4 |
| `dizzy.png` | 2 | After being thrown hard | | 4 |
| `happy.png` | 1–2 | Hop after being clicked | | 6 |
| `juggling.png` | 2 | Juggling at a screen edge: frame 1 left arm up, frame 2 right arm up (each raised arm throws; the balls are drawn by code) | | 4 |
| `sip.png` | 1–4 | Sipping the coffee after a coffee run *(uses `idle.png` until drawn)* | `coffee` | 3 |
| `working.png` | 2–4 | Claude is editing files or running commands | `laptop` | 6 |
| `reading.png` | 2–4 | Claude is reading/searching files *(uses `working.png` until drawn)* | `book` | 3 |
| `searching.png` | 2–4 | Claude is searching the web *(uses `working.png` until drawn)* | `binoculars` | 3 |
| `thinking.png` | 2–4 | You just sent a prompt / Claude is delegating *(uses `working.png` until drawn)* | | 3 |
| `alert.png` | 2 | Claude needs you | | 3 |
| `celebrate.png` | 2–3 | Claude finished | | 6 |

**Previewing:** set `CLAWD_DEMO=juggling` (or `coffee`, `jump`, `sleep`) before starting the pet and it does
that as soon as it's idle — handy for checking a new sprite. (While Claude Code is busy, Clawd is "working"
instead of idle, so the demo waits until Claude finishes.)

Speeds and held objects live in `SpriteFrames.cs` (`Fps`, `HeldObjects`) if you want to change them.
An animation that borrows another's art keeps its own object if that object exists (`sip` keeps the coffee),
otherwise it borrows the other animation's object too (`reading` shows the laptop until there's a `book`).

### Coffee run

After about 5 minutes of Claude working (`CLAWD_COFFEE_AFTER=<seconds>` to change it), Clawd walks off
the nearest screen edge that doesn't lead onto another monitor, comes back **carrying `objects/coffee.png`
in its walk animation**, stops to play `sip.png`, carries the cup back off the same edge, and returns
empty-handed a few seconds later. To put the cup in Clawd's hand
while walking, add a marker to each frame of `walk.png` — it's ignored on ordinary walks, when nothing is held.

## Held objects (`sprites/objects/`)

Single images, facing right like Clawd, e.g. `objects/laptop.png`. For an animated object, name it
`<name>@<frames>.png` with the frames side by side (e.g. `coffee@3.png` for steam). Any size —
empty space around the art is trimmed off when loading, so drawing on a 16×16 canvas is fine.

**Where it goes** — by default the object sits on the ground just in front of Clawd. To place it
yourself, paint **one marker pixel** in each of Clawd's frames:

- **Magenta `#FF00FF`** — the object goes here, **in front of** Clawd
- **Cyan `#00FFFF`** — the object goes here, **behind** Clawd

Marker pixels are removed when loading: they're never drawn and never catch clicks — so put them on
pixels that should be empty anyway, e.g. one pixel below Clawd's hand *and* one pixel below the object's
handle (both offsets cancel out, so the handle still lands on the hand).

**Several marker pixels mark their centre.** Two stacked markers put the anchor half a pixel below the
top one — handy when a 2-pixel hand has to hold the middle of a 3-pixel handle.
The object is lined up by its bottom-centre, or by a magenta pixel in the *object's* image if it has one
(its "grip" — e.g. the corner of the laptop's base, or a cup's handle).

## Emotes (`sprites/emotes/`)

Any strip of square frames, e.g. `emotes/sixseven.png`. Clawd occasionally plays one while idle
(twice through, at least 2 s). Emotes are **not mirrored**, so numbers and letters in them stay readable.

## Effects (`sprites/fx/`)

Single images, drawn around Clawd and **never mirrored**. Positioned relative to the top of Clawd's
head in the current frame. Add your own dark outline if you want them readable on light *and* dark
backgrounds (the built-in ones get one automatically).

| File | Used for |
|---|---|
| `fx/z.png` | Sleeping Zs, floating up (a Z needs at least 5 rows or the diagonal reads as an "I") |
| `fx/alert.png` | "!" above the head when Claude needs you |
| `fx/sparkle.png` | Sparkles when Claude finishes |
| `fx/star.png` | Stars circling the head when dizzy |
| `fx/ball.png` | The juggling balls (used for all three; the built-in ones are yellow, blue and white) |
