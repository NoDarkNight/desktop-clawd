# Sprites

Put your PNGs in this folder. Any animation without a PNG uses the built-in placeholder,
so you can replace them one at a time. After saving, right-click the tray icon →
**Reload sprites** (no rebuild needed).

`templates/` has the current placeholders in the right format — open one in Piskel and draw over it.
Regenerate them with `dotnet run -- --export-templates sprites/templates`.

## Format

- **PNG, transparent background.** One art pixel = one image pixel (don't upscale; the pet does that).
- **16×16 per frame**, character facing **right**, feet on the **bottom row**. Frames may be larger
  if needed (the window grows to fit), but all frames in one file must be the same size.
- **Frames side by side in one horizontal strip**, e.g. a 4-frame walk is 64×16.
  In Piskel: *Export → PNG → Spritesheet*, 1 row.
- **No anti-aliasing / soft brushes** — every pixel fully opaque or fully transparent.

## Files

| File | Frames | When it plays | Speed (fps) |
|---|---|---|---|
| `idle.png` | 1–4 | Standing around | 2 |
| `blink.png` | 1 | Eyes closed (flashed briefly while idle) | – |
| `walk.png` | 2–4 | Walking | 8 |
| `sleep.png` | 1–2 | Sleeping | 1.5 |
| `held.png` | 1–2 | Being dragged | 4 |
| `fall.png` | 1 | Falling / thrown | 4 |
| `land.png` | 1 | Squash on landing (~0.1 s) | – |
| `happy.png` | 1–2 | Hop after being clicked | 6 |
| `working.png` | 2–4 | Claude Code is working | 6 |
| `alert.png` | 2 | Claude Code needs you | 3 |
| `celebrate.png` | 2–3 | Claude Code finished | 6 |

Speeds live in `SpriteFrames.Fps` if you want to change them.
