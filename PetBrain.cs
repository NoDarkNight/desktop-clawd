using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DesktopClawd;

/// <summary>A monitor's usable area (excluding the taskbar), in physical screen pixels.</summary>
public readonly record struct WorkArea(double Left, double Top, double Right, double Bottom);

/// <summary>What the UI layer must provide to the brain. All positions are physical pixels.</summary>
public interface IPetHost
{
    /// <summary>Physical pixels per DIP for the monitor the pet is on.</summary>
    double Scale { get; }
    /// <summary>Size of the character itself (not including room for effects).</summary>
    double PxWidth { get; }
    double PxHeight { get; }
    WorkArea WorkAreaAt(double x, double y);

    /// <summary>Window ledges from the most recent scan.</summary>
    IReadOnlyList<Surface> Surfaces { get; }
    bool TryGetWindowOrigin(nint id, out double left, out double top);
    (double X, double Y)? CursorPosition { get; }
    TimeSpan? UserIdle { get; }

    /// <summary>Custom emote animations available to play while idle.</summary>
    IReadOnlyList<EmoteInfo> Emotes { get; }

    /// <summary>Whether a point is on any monitor (used to find a screen edge with nothing beyond it).</summary>
    bool IsOnScreen(double x, double y);
}

/// <summary>An emote animation and how long one performance of it lasts, in seconds.</summary>
public readonly record struct EmoteInfo(string Name, double Duration);

public enum PetState { Idle, Walk, Sleep, Held, Falling, Hop, Jump, Land, Slide, Dizzy, Wake, Emote, Sip, CoffeeOut, CoffeeAway, CoffeeBack, CoffeeReturn, CoffeeDropOff, CoffeeHome, Working, Alert, Celebrate }

/// <summary>What Claude Code is doing, as reported by its hooks.</summary>
public enum ClaudeActivity { None, Working, NeedsAttention, Done }

/// <summary>What kind of work Claude is doing, judged from the tool it's using.</summary>
public enum WorkKind { Typing, Reading, Searching, Thinking }

public static class ClaudeTools
{
    public static WorkKind KindOf(string? toolName) => toolName switch
    {
        null or "" => WorkKind.Thinking, // prompt submitted, no tool chosen yet
        "Read" or "Grep" or "Glob" or "LS" or "NotebookRead" => WorkKind.Reading,
        "WebFetch" or "WebSearch" => WorkKind.Searching,
        "Task" or "Agent" => WorkKind.Thinking,
        _ => WorkKind.Typing, // edits, shell commands, MCP tools, ...
    };
}

/// <summary>Overlay effects drawn around (not mirrored with) the character.</summary>
public enum PetEffect { None, Sleeping, Alert, Sparkles, Dizzy }

/// <summary>
/// Framework-independent behaviour and physics.
/// Positions and velocities are in physical screen pixels; tuning constants are in DIPs.
/// (_x, _y) is the top-left of the character canvas.
/// </summary>
public sealed class PetBrain(IPetHost host)
{
    private const double WalkSpeed = 45;       // DIP/s
    private const double Gravity = 1800;       // DIP/s^2
    private const double HopSpeed = 420;       // DIP/s
    private const double MaxThrow = 2500;      // DIP/s
    private const double DragThreshold = 4;    // DIP
    private const double WallBounce = 0.5;     // fraction of speed kept when hitting a side or the top
    private const double LandTime = 0.12;      // s of squash after a hard landing
    private const double SquashTime = 0.07;    // s of squash on each bounce
    // Landing tiers, by vertical speed at impact (DIP/s):
    //   below GentleSpeed: just lands
    //   GentleSpeed..BounceSpeed: squashes, no bounce
    //   BounceSpeed..DazeSpeed: bounces (squashing on each impact) until it's slow enough to settle
    //   above DazeSpeed: knocked silly — dazed while bouncing and for a while after, longer the harder it hit
    private const double GentleSpeed = 250;
    private const double BounceSpeed = 900;
    private const double DazeSpeed = 1500;
    private const double DazeMinTime = 1.5, DazeMaxTime = 5; // s
    private const double DazeSecondsPerExtraSpeed = 2.0 / 1000; // s of extra daze per DIP/s above DazeSpeed
    private const double BounceRestitution = 0.4;   // fraction of vertical speed kept per bounce
    private const double BounceKeepHorizontal = 0.85; // fraction of sideways speed kept per bounce
    private const double SlideMinSpeed = 60;   // DIP/s; slower than this just stops (so walk-offs don't skid)
    private const double SlideMaxSpeed = 900;  // DIP/s; caps how far a hard throw skids
    private const double SlideFriction = 1600; // DIP/s^2
    private const double SlideStopSpeed = 15;  // DIP/s
    private const double WakeTime = 1.0;       // s of stretching after waking up
    private const double EdgeWalkOffChance = 0.4; // chance a walk carries on off a window's edge
    private const double JumpChance = 0.2;     // chance an idle decision is "jump onto a window"
    private const double EmoteChance = 0.1;    // chance an idle decision is "play an emote"
    private const double MinJumpUp = 30, MaxJumpUp = 320, MaxJumpSide = 360, JumpClearance = 36; // DIP
    private const double CursorWatchRange = 220; // DIP

    // How long each Claude activity lasts without a new event, in seconds.
    private const double WorkingTimeout = 600;
    private const double AttentionTimeout = 300;
    private const double CelebrateTime = 3;

    // Coffee run: after enough time working, Clawd walks off the nearest open screen edge, comes back
    // carrying a coffee, sips it, and gets back to work.
    private const double CoffeeStartRate = 1.0 / 20; // per second once it's earned one (so ~20 s on average)
    private const double CoffeeHurry = 1.4;          // walks off faster than usual
    private const double CoffeeAwayMin = 6, CoffeeAwayMax = 12; // s off-screen
    private const double CoffeeReturnMin = 10, CoffeeReturnMax = 40;   // DIP inside the edge where it stops (stays near the edge)
    private const double SipTime = 3.5;              // s
    private const double CoffeeDropOffMin = 2, CoffeeDropOffMax = 4; // s off-screen returning the cup

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rng = new();

    private double _lastTick;
    private double _x, _y, _vx, _vy;
    private PetState _state = PetState.Falling;
    private double _stateTime;
    private double _blinkIn = 3, _blinkFor;
    private double _animStart;
    private Surface _surface;          // what it's standing on; Id 0 = the screen floor
    private bool _walkOffEdges;
    private bool _dizzyOnLanding;
    private bool _droppingIn = true; // the start-up drop never dazes
    private double _dazeTime;
    private int _bounces;
    private double _squashFor;

    private ClaudeActivity _claude;
    private WorkKind _workKind;
    private double _claudeTime;
    private string? _emote;
    private double _workedSinceCoffee; // s of Claude working since the last coffee
    private WorkArea _coffeeArea;
    private int _coffeeDir;           // -1 = left edge, +1 = right edge
    private double _coffeeStopX;

    private bool _pressed, _dragging;
    private double _pressX, _pressY, _grabX, _grabY;
    private (double X, double Y, double T) _dragSample, _prevDragSample;

    public double X => _x;
    public double Y => _y;
    public Anim Animation { get; private set; } = Anim.Fall;
    /// <summary>Seconds since the current animation started; the renderer picks the frame from this.</summary>
    public double AnimTime => Now - _animStart;
    public bool FacingLeft { get; private set; }
    public bool IsPressed => _pressed;
    /// <summary>The emote being performed, when <see cref="Animation"/> is <see cref="Anim.Emote"/>.</summary>
    public string? EmoteName => _state == PetState.Emote ? _emote : null;

    /// <summary>Seconds without keyboard/mouse input before it naps.</summary>
    public double SleepAfterIdle { get; init; } = 90;

    /// <summary>Seconds of Claude working before Clawd goes for a coffee.</summary>
    public double CoffeeAfter { get; init; } = 300;

    /// <summary>An object carried outside its animation's usual one (the coffee, to and from the sip).</summary>
    public string? CarriedObject => _state is PetState.CoffeeBack or PetState.CoffeeReturn ? "coffee" : null;
    public PetEffect Effect => _state switch
    {
        PetState.Sleep => PetEffect.Sleeping,
        PetState.Dizzy => PetEffect.Dizzy,
        PetState.Falling or PetState.Slide when Dazed => PetEffect.Dizzy,
        PetState.Alert => PetEffect.Alert,
        PetState.Celebrate => PetEffect.Sparkles,
        PetState.Hop when _claude == ClaudeActivity.NeedsAttention => PetEffect.Alert,
        PetState.Hop when _claude == ClaudeActivity.Done => PetEffect.Sparkles,
        _ => PetEffect.None,
    };

    /// <summary>A hard throw has knocked it silly: still bouncing or skidding, dizzy once it stops.</summary>
    private bool Dazed => _dizzyOnLanding && _state is PetState.Falling or PetState.Slide;

    private double S => host.Scale;
    private double W => host.PxWidth;
    private double H => host.PxHeight;
    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Drop in from the top-centre of the given area.</summary>
    public void Start(WorkArea area)
    {
        _x = (area.Left + area.Right) / 2 - W / 2;
        _y = area.Top;
        _lastTick = Now;
    }

    public void OnClaude(ClaudeActivity activity, WorkKind kind = WorkKind.Typing)
    {
        _claude = activity;
        if (activity == ClaudeActivity.Working) _workKind = kind;
        _claudeTime = activity switch
        {
            ClaudeActivity.Working => WorkingTimeout,
            ClaudeActivity.NeedsAttention => AttentionTimeout,
            ClaudeActivity.Done => CelebrateTime,
            _ => 0,
        };
    }

    public void Tick()
    {
        var now = Now;
        var dt = Math.Min(now - _lastTick, 0.1);
        _lastTick = now;

        var area = host.WorkAreaAt(_x + W / 2, _y + H / 2);
        _stateTime -= dt;

        if (_claude != ClaudeActivity.None && (_claudeTime -= dt) <= 0)
            _claude = ClaudeActivity.None;
        if (_claude == ClaudeActivity.Working) _workedSinceCoffee += dt;

        switch (_state)
        {
            case PetState.Falling:
            case PetState.Hop:
            case PetState.Jump:
                TickAirborne(area, dt);
                break;

            case PetState.Held:
                break;

            case PetState.CoffeeOut:
            case PetState.CoffeeAway:
            case PetState.CoffeeBack:
            case PetState.CoffeeReturn:
            case PetState.CoffeeDropOff:
            case PetState.CoffeeHome:
                TickCoffeeRun(dt);
                break;

            default:
                if (!StandOnSurface(area)) { Enter(PetState.Falling); break; }
                TickGrounded(area, dt);
                break;
        }

        if (_squashFor > 0) _squashFor -= dt;
        UpdateBlink(dt);
        RefreshAnimation();
    }

    // --- Standing, walking and deciding what to do ---

    /// <summary>Keeps the pet on its surface, riding along if that window moved. False if the ground is gone.</summary>
    private bool StandOnSurface(WorkArea area)
    {
        if (_surface.Id == 0)
        {
            _surface = Floor(area);
        }
        else
        {
            if (!host.TryGetWindowOrigin(_surface.Id, out var ox, out var oy)) return false;
            _x += ox - _surface.OwnerLeft; // ride along with the window

            // Re-find the visible ledge (it may have moved, shrunk or been covered since the last scan).
            var cx = _x + W / 2;
            Surface? found = null;
            foreach (var s in host.Surfaces)
            {
                if (s.Id != _surface.Id || s.Bottom != _surface.Bottom) continue;
                var shifted = s.Shift(ox - s.OwnerLeft, oy - s.OwnerTop);
                if (cx >= shifted.Left && cx <= shifted.Right) { found = shifted; break; }
            }
            if (found is null) return false;
            _surface = found.Value;
        }

        _y = _surface.Top - H;
        return true;
    }

    private void TickGrounded(WorkArea area, double dt)
    {
        switch (_state)
        {
            case PetState.Slide:
                TickSlide(area, dt);
                return;
            case PetState.Land:
                if (_stateTime <= 0) AfterLanding();

                return;
            case PetState.Dizzy:
            case PetState.Wake:
            case PetState.Emote:
                if (_stateTime <= 0) Enter(PetState.Idle, 0.5);
                return;
            case PetState.Sip:
                if (_stateTime <= 0) StartCoffeeReturn();
                return;
        }

        // Claude activity takes priority over everything else.
        PetState? wanted = _claude switch
        {
            ClaudeActivity.Working => PetState.Working,
            ClaudeActivity.NeedsAttention => PetState.Alert,
            ClaudeActivity.Done => PetState.Celebrate,
            _ => null,
        };
        if (wanted is { } claudeState)
        {
            // A long stretch of work earns a coffee break.
            if (claudeState == PetState.Working && _state == PetState.Working && _surface.Id == 0
                && _workedSinceCoffee >= CoffeeAfter && _rng.NextDouble() < CoffeeStartRate * dt
                && TryStartCoffeeRun(area))
                return;
            // Alert hops every so often to get your attention; celebrate hops continuously.
            if (_state != claudeState) Enter(claudeState, claudeState == PetState.Alert ? 1.2 : 0);
            if (claudeState != PetState.Working && _stateTime <= 0)
                StartHop(claudeState == PetState.Celebrate ? 0.8 : 0.6);
            return;
        }
        if (_state is PetState.Working or PetState.Alert or PetState.Celebrate)
        {
            Enter(PetState.Idle, 1);
            return;
        }

        // Nap while you're away; wake up as soon as you touch the mouse or keyboard.
        if (host.UserIdle is { } idle)
        {
            if (_state == PetState.Sleep)
            {
                if (idle.TotalSeconds < 1) Enter(PetState.Wake, WakeTime);
                return;
            }
            if (idle.TotalSeconds >= SleepAfterIdle && _state is PetState.Idle or PetState.Walk)
            {
                Enter(PetState.Sleep);
                return;
            }
        }

        if (_state == PetState.Walk)
        {
            TickWalk(area, dt);
            return;
        }

        if (_state == PetState.Idle) WatchCursor();
        if (_stateTime <= 0) ChooseNextAction(area);
    }

    private void TickWalk(WorkArea area, double dt)
    {
        _x += _vx * dt;
        FacingLeft = _vx < 0;

        if (_surface.Id == 0)
        {
            if (_x < area.Left) { _x = area.Left; _vx = Math.Abs(_vx); }
            if (_x + W > area.Right) { _x = area.Right - W; _vx = -Math.Abs(_vx); }
        }
        else if (!_walkOffEdges)
        {
            // Turn around before the middle of the pet passes the edge.
            var min = _surface.Left - W * 0.2;
            var max = _surface.Right - W * 0.8;
            if (max <= min) { Enter(PetState.Idle, 1); return; }
            if (_x < min) { _x = min; _vx = Math.Abs(_vx); }
            if (_x > max) { _x = max; _vx = -Math.Abs(_vx); }
        }
        // Otherwise keep going: StandOnSurface fails next tick and it walks off the edge.

        if (_stateTime <= 0) Enter(PetState.Idle, 1.5 + _rng.NextDouble() * 3);
    }

    private void WatchCursor()
    {
        if (host.CursorPosition is not { } c) return;
        var dx = c.X - (_x + W / 2);
        var dy = c.Y - (_y + H / 2);
        var range = CursorWatchRange * S;
        if (Math.Abs(dx) < range && Math.Abs(dy) < range && Math.Abs(dx) > W * 0.25)
            FacingLeft = dx < 0;
    }

    private void ChooseNextAction(WorkArea area)
    {
        if (_state == PetState.Sleep)
        {
            Enter(PetState.Idle, 1.5 + _rng.NextDouble() * 3);
            return;
        }

        var roll = _rng.NextDouble();
        if (roll < JumpChance && TryStartJump(area)) return;
        if (roll < JumpChance + EmoteChance && host.Emotes.Count > 0)
        {
            var emote = host.Emotes[_rng.Next(host.Emotes.Count)];
            _emote = emote.Name;
            Enter(PetState.Emote, emote.Duration);
            return;
        }
        if (host.UserIdle is null && roll > 0.93)
        {
            // No idle detection on this platform: take the occasional random nap instead.
            Enter(PetState.Sleep, 10 + _rng.NextDouble() * 15);
            return;
        }
        if (roll < 0.45) Enter(PetState.Idle, 1.5 + _rng.NextDouble() * 3);
        else Enter(PetState.Walk, 2 + _rng.NextDouble() * 5);
    }

    /// <summary>Leaps up onto a nearby window ledge, if there's one in reach.</summary>
    private bool TryStartJump(WorkArea area)
    {
        var feet = _y + H;
        var cx = _x + W / 2;
        var options = new List<(Surface Target, double X)>();
        foreach (var s in host.Surfaces)
        {
            if (s.Id == _surface.Id && s.Bottom == _surface.Bottom) continue;
            var rise = feet - s.Top;
            if (rise < MinJumpUp * S || rise > MaxJumpUp * S) continue;
            if (s.Right - s.Left < W * 2.5) continue;
            if (s.Top - H - JumpClearance * S < area.Top) continue;
            var landX = Math.Clamp(cx, s.Left + W, s.Right - W);
            if (Math.Abs(landX - cx) > MaxJumpSide * S) continue;
            options.Add((s, landX));
        }
        if (options.Count == 0) return false;

        // Ballistic arc peaking a little above the target ledge.
        var (target, targetX) = options[_rng.Next(options.Count)];
        var g = Gravity * S;
        var apex = target.Top - JumpClearance * S;
        _vy = -Math.Sqrt(2 * g * (feet - apex));
        var flightTime = -_vy / g + Math.Sqrt(2 * (target.Top - apex) / g);
        _vx = (targetX - cx) / flightTime;
        FacingLeft = _vx < 0;
        Enter(PetState.Jump);
        return true;
    }

    // --- Coffee run ---

    /// <summary>Heads for the nearest screen edge that doesn't lead onto another monitor.</summary>
    private bool TryStartCoffeeRun(WorkArea area)
    {
        var midY = area.Bottom - H / 2;
        var leftOpen = !host.IsOnScreen(area.Left - W, midY);
        var rightOpen = !host.IsOnScreen(area.Right + W, midY);
        if (!leftOpen && !rightOpen) return false;

        var cx = _x + W / 2;
        _coffeeDir = leftOpen && rightOpen ? (cx - area.Left < area.Right - cx ? -1 : 1) : leftOpen ? -1 : 1;
        _coffeeArea = area;
        _workedSinceCoffee = 0;
        _vx = _coffeeDir * WalkSpeed * CoffeeHurry * S;
        FacingLeft = _coffeeDir < 0;
        Enter(PetState.CoffeeOut);
        return true;
    }

    /// <summary>
    /// The trip, on the floor it left from: walk off-screen (CoffeeOut), fetch it (CoffeeAway), walk back
    /// in with the cup (CoffeeBack), sip (Sip, a grounded state), take the cup back off-screen
    /// (CoffeeReturn), drop it off (CoffeeDropOff), and walk back in empty-handed (CoffeeHome).
    /// </summary>
    private void TickCoffeeRun(double dt)
    {
        var area = _coffeeArea;
        _y = area.Bottom - H;

        switch (_state)
        {
            case PetState.CoffeeOut:
                if (WalkedOff(area, dt)) Enter(PetState.CoffeeAway, CoffeeAwayMin + _rng.NextDouble() * (CoffeeAwayMax - CoffeeAwayMin));
                break;

            case PetState.CoffeeReturn:
                if (WalkedOff(area, dt)) Enter(PetState.CoffeeDropOff, CoffeeDropOffMin + _rng.NextDouble() * (CoffeeDropOffMax - CoffeeDropOffMin));
                break;

            case PetState.CoffeeAway:
            case PetState.CoffeeDropOff:
                // Hurries back early if Claude needs you.
                if (_stateTime > 0 && _claude != ClaudeActivity.NeedsAttention) break;
                var walkIn = (CoffeeReturnMin + _rng.NextDouble() * (CoffeeReturnMax - CoffeeReturnMin)) * S;
                _x = _coffeeDir < 0 ? area.Left - W : area.Right;
                _coffeeStopX = _coffeeDir < 0 ? area.Left + walkIn : area.Right - W - walkIn;
                _vx = -_coffeeDir * WalkSpeed * S;
                FacingLeft = _vx < 0;
                Enter(_state == PetState.CoffeeAway ? PetState.CoffeeBack : PetState.CoffeeHome);
                break;

            case PetState.CoffeeBack:
            case PetState.CoffeeHome:
                _x += _vx * dt;
                if (_vx > 0 ? _x >= _coffeeStopX : _x <= _coffeeStopX)
                {
                    _vx = 0;
                    _surface = Floor(area);
                    if (_state == PetState.CoffeeBack) Enter(PetState.Sip, SipTime);
                    else Enter(PetState.Idle, 0.5); // back to whatever it was doing (work resumes if Claude still is)
                }
                break;
        }
    }

    /// <summary>Walks towards the run's screen edge; true once fully off-screen.</summary>
    private bool WalkedOff(WorkArea area, double dt)
    {
        _x += _vx * dt;
        return _coffeeDir < 0 ? _x + W < area.Left : _x > area.Right;
    }

    /// <summary>After sipping, takes the cup back the way it came.</summary>
    private void StartCoffeeReturn()
    {
        _vx = _coffeeDir * WalkSpeed * S;
        FacingLeft = _coffeeDir < 0;
        Enter(PetState.CoffeeReturn);
    }

    // --- In the air ---

    private void TickAirborne(WorkArea area, double dt)
    {
        var prevFeet = _y + H;
        _vy += Gravity * S * dt;
        _x += _vx * dt;
        _y += _vy * dt;
        if (_x < area.Left) { _x = area.Left; _vx = Math.Abs(_vx) * WallBounce; }
        if (_x + W > area.Right) { _x = area.Right - W; _vx = -Math.Abs(_vx) * WallBounce; }
        if (_y < area.Top) { _y = area.Top; _vy = Math.Abs(_vy) * WallBounce; }
        if (_vy < 0) return;

        // Land on the first ledge crossed this tick, or the floor.
        var feet = _y + H;
        var cx = _x + W / 2;
        var floor = Floor(area);
        Surface? landing = null;
        foreach (var s in host.Surfaces)
        {
            if (s.Top >= prevFeet - 1 && s.Top <= feet && cx >= s.Left && cx <= s.Right
                && (landing is null || s.Top < landing.Value.Top))
                landing = s;
        }
        if (landing is null && feet >= floor.Top) landing = floor;
        if (landing is not { } ground) return;

        _surface = ground;
        _y = ground.Top - H;

        // Hops and deliberate jumps land cleanly where they aimed.
        if (_state != PetState.Falling)
        {
            var impact = _vy;
            _vx = _vy = 0;
            Settle(impact);
            return;
        }

        // Only the first impact of a fall decides dizziness; the bounces after it are gentler.
        if (_bounces == 0)
        {
            _dizzyOnLanding = !_droppingIn && _vy > DazeSpeed * S;
            _dazeTime = Math.Clamp(DazeMinTime + (_vy / S - DazeSpeed) * DazeSecondsPerExtraSpeed, DazeMinTime, DazeMaxTime);
            _droppingIn = false;
        }

        // Bounce, losing height each time, with a squash on each impact that isn't gentle.
        if (_vy > GentleSpeed * S) _squashFor = SquashTime;
        if (_vy > BounceSpeed * S)
        {
            _vy = -_vy * BounceRestitution;
            _vx *= BounceKeepHorizontal;
            _bounces++;
            return;
        }

        // Settled vertically: skid to a stop if there's sideways speed left.
        var finalImpact = _vy;
        _vy = 0;
        _bounces = 0;
        if (Math.Abs(_vx) > SlideMinSpeed * S)
        {
            var max = SlideMaxSpeed * S;
            _vx = Math.Clamp(_vx, -max, max);
            FacingLeft = _vx < 0;
            Enter(PetState.Slide);
            return;
        }
        _vx = 0;
        Settle(finalImpact);
    }

    /// <summary>Finishes a landing: a squash unless it was gentle (or it's already flat and dazed), then dazed or idle.</summary>
    private void Settle(double impact)
    {
        if (impact > GentleSpeed * S && !_dizzyOnLanding) Enter(PetState.Land, LandTime); // Land then calls AfterLanding
        else AfterLanding();
    }

    private void AfterLanding()
    {
        if (_dizzyOnLanding)
        {
            _dizzyOnLanding = false;
            Enter(PetState.Dizzy, _dazeTime);
        }
        else Enter(PetState.Idle, 0.4);
    }

    /// <summary>Skids along the surface, slowing down; can slide off a window's edge or bounce off the screen's sides.</summary>
    private void TickSlide(WorkArea area, double dt)
    {
        var slowdown = SlideFriction * S * dt;
        _vx = Math.Abs(_vx) <= slowdown ? 0 : _vx - Math.Sign(_vx) * slowdown;
        _x += _vx * dt;

        if (_surface.Id == 0)
        {
            if (_x < area.Left) { _x = area.Left; _vx = Math.Abs(_vx) * WallBounce; }
            if (_x + W > area.Right) { _x = area.Right - W; _vx = -Math.Abs(_vx) * WallBounce; }
        }
        // On a window, sliding past the edge makes StandOnSurface fail next tick, and it falls off with its momentum.

        if (Math.Abs(_vx) < SlideStopSpeed * S)
        {
            _vx = 0;
            AfterLanding(); // the impact already happened before the skid, so no squash now
        }
    }

    private static Surface Floor(WorkArea area) => new(0, false, area.Left, area.Right, area.Bottom, 0, 0);

    // --- Mouse ---

    /// <summary>Left button pressed at a screen point (physical pixels).</summary>
    public void Press(double screenX, double screenY)
    {
        _pressed = true;
        _dragging = false;
        _pressX = screenX;
        _pressY = screenY;
        _grabX = screenX - _x;
        _grabY = screenY - _y;
    }

    /// <summary>Pointer moved while pressed. Returns true if the pet moved.</summary>
    public bool Move(double screenX, double screenY)
    {
        if (!_pressed) return false;

        if (!_dragging)
        {
            var dx = screenX - _pressX;
            var dy = screenY - _pressY;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold * S) return false;
            _dragging = true;
            _state = PetState.Held;
            _dragSample = _prevDragSample = (_x, _y, Now);
        }

        _x = screenX - _grabX;
        _y = screenY - _grabY;
        RefreshAnimation();

        var now = Now;
        if (now - _dragSample.T > 0.03)
        {
            _prevDragSample = _dragSample;
            _dragSample = (_x, _y, now);
        }
        return true;
    }

    /// <summary>Button released or capture lost. A drag becomes a throw; a click becomes a hop.</summary>
    public void Release()
    {
        if (!_pressed) return;
        _pressed = false;

        if (_dragging)
        {
            _dragging = false;
            var dt = Math.Max(_dragSample.T - _prevDragSample.T, 0.001);
            var max = MaxThrow * S;
            _vx = Math.Clamp((_dragSample.X - _prevDragSample.X) / dt, -max, max);
            _vy = Math.Clamp((_dragSample.Y - _prevDragSample.Y) / dt, -max, max);
            if (Now - _dragSample.T > 0.1) _vx = _vy = 0;
            _bounces = 0;
            _dizzyOnLanding = false;
            _droppingIn = false;
            Enter(PetState.Falling);
        }
        else if (_state is not (PetState.Held or PetState.Falling or PetState.Hop or PetState.Jump))
        {
            // Clicking acknowledges a "needs attention" alert.
            if (_claude == ClaudeActivity.NeedsAttention) _claude = ClaudeActivity.None;
            StartHop(1);
        }
    }

    // --- Helpers ---

    private void StartHop(double strength)
    {
        _vx = 0;
        _vy = -HopSpeed * strength * S;
        Enter(PetState.Hop);
    }

    private void Enter(PetState state, double duration = 0)
    {
        _state = state;
        _stateTime = duration;
        if (state == PetState.Walk)
        {
            _vx = (_rng.Next(2) == 0 ? -1 : 1) * WalkSpeed * S;
            _walkOffEdges = _rng.NextDouble() < EdgeWalkOffChance;
        }
    }

    private void UpdateBlink(double dt)
    {
        if (_blinkFor > 0) { _blinkFor -= dt; return; }
        _blinkIn -= dt;
        if (_blinkIn <= 0)
        {
            _blinkFor = 0.15;
            _blinkIn = 2 + _rng.NextDouble() * 4;
        }
    }

    private void RefreshAnimation()
    {
        var anim = _state switch
        {
            PetState.Walk => Anim.Walk,
            PetState.Sleep => Anim.Sleep,
            PetState.Held => Anim.Held,
            PetState.Falling => Dazed ? Anim.Dizzy : _squashFor > 0 ? Anim.Land : Anim.Fall,
            PetState.Slide => Dazed ? Anim.Dizzy : Anim.Slide,
            PetState.Jump => Anim.Jump,
            PetState.Land => Anim.Land,
            PetState.Dizzy => Anim.Dizzy,
            PetState.Wake => Anim.Wake,
            PetState.Emote => Anim.Emote,
            PetState.Sip => Anim.Sip,
            PetState.CoffeeOut or PetState.CoffeeAway or PetState.CoffeeBack or PetState.CoffeeReturn
                or PetState.CoffeeDropOff or PetState.CoffeeHome => Anim.Walk,
            PetState.Working => _workKind switch
            {
                WorkKind.Reading => Anim.Reading,
                WorkKind.Searching => Anim.Searching,
                WorkKind.Thinking => Anim.Thinking,
                _ => Anim.Working,
            },
            PetState.Alert => Anim.Alert,
            PetState.Celebrate => Anim.Celebrate,
            PetState.Hop => _claude switch
            {
                ClaudeActivity.NeedsAttention => Anim.Alert,
                ClaudeActivity.Done => Anim.Celebrate,
                _ => Anim.Happy,
            },
            _ => _blinkFor > 0 ? Anim.Blink : Anim.Idle,
        };
        if (anim == Animation) return;
        Animation = anim;
        _animStart = Now;
    }
}
