# Sprites

Put your PNGs in this folder. Any animation without a PNG uses the built-in placeholder,
so you can replace them one at a time. After saving, right-click the tray icon →
**Reload sprites** (no rebuild needed).

`templates/` has the current placeholders in the right format — open one in Piskel and draw over it.
Regenerate them with `dotnet run -- --export-templates sprites/templates`.

## Character animations

Draw **only Clawd** in these — no Zs, "!" or sparkles. Effects are separate (see below) because
character frames get mirrored when Clawd faces left, which would turn a Z backwards.

- **PNG, transparent background.** One art pixel = one image pixel (don't upscale; the pet does that).
- **16×16 per frame**, character facing **right**, feet on the **bottom row**. Frames may be larger
  if needed (the window grows to fit), but all frames in one file must be the same size.
- **Frames side by side in one horizontal strip**, e.g. a 4-frame walk is 64×16.
  In Piskel: *Export → PNG → Spritesheet*, 1 row.
- **No anti-aliasing / soft brushes** — every pixel fully opaque or fully transparent.
  Only opaque pixels catch the mouse; clicks on transparent pixels go to whatever is behind.

| File | Frames | When it plays | Speed (fps) |
|---|---|---|---|
| `idle.png` | 1–4 | Standing around | 2 |
| `blink.png` | 1 | Eyes closed (flashed briefly while idle) | – |
| `walk.png` | 2–4 | Walking | 8 |
| `sleep.png` | 1–2 | Napping while you're away | 0.8 |
| `wake.png` | 2 | Stretching as it wakes up | 2 |
| `held.png` | 1–2 | Being dragged | 4 |
| `fall.png` | 1 | Falling / thrown | 4 |
| `jump.png` | 1–2 | Leaping up onto a window | 4 |
| `land.png` | 1 | Squash on landing (~0.1 s) | – |
| `dizzy.png` | 2 | After being thrown hard | 4 |
| `happy.png` | 1–2 | Hop after being clicked | 6 |
| `working.png` | 2–4 | Claude Code is working | 6 |
| `alert.png` | 2 | Claude Code needs you | 3 |
| `celebrate.png` | 2–3 | Claude Code finished | 6 |

Speeds live in `SpriteFrames.Fps` if you want to change them.

## Effects (`fx/` folder)

Single images (not strips), drawn around Clawd and **never mirrored**, so draw them the way they
should read. Any size works; they're positioned relative to the top of Clawd's head in the current frame.
Add your own dark outline if you want them readable on light *and* dark backgrounds
(the built-in ones get one automatically).

| File | Used for |
|---|---|
| `fx/z.png` | Sleeping Zs, floating up (a Z needs at least 5 rows or the diagonal reads as an "I") |
| `fx/alert.png` | "!" above the head when Claude needs you |
| `fx/sparkle.png` | Sparkles when Claude finishes |
| `fx/star.png` | Stars circling the head when dizzy |
