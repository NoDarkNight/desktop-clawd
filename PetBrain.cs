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
}

public enum PetState { Idle, Walk, Sleep, Held, Falling, Hop, Jump, Land, Dizzy, Wake, Working, Alert, Celebrate }

/// <summary>What Claude Code is doing, as reported by its hooks.</summary>
public enum ClaudeActivity { None, Working, NeedsAttention, Done }

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
    private const double LandTime = 0.12;      // s of squash after landing
    private const double DizzySpeed = 1300;    // DIP/s landing speed that leaves it dizzy
    private const double DizzyTime = 1.8;      // s
    private const double WakeTime = 1.0;       // s of stretching after waking up
    private const double EdgeWalkOffChance = 0.4; // chance a walk carries on off a window's edge
    private const double JumpChance = 0.2;     // chance an idle decision is "jump onto a window"
    private const double MinJumpUp = 30, MaxJumpUp = 320, MaxJumpSide = 360, JumpClearance = 36; // DIP
    private const double CursorWatchRange = 220; // DIP

    // How long each Claude activity lasts without a new event, in seconds.
    private const double WorkingTimeout = 600;
    private const double AttentionTimeout = 300;
    private const double CelebrateTime = 3;

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
    private bool _thrown; // only a throw (not a drop-in or walking off a ledge) can leave it dizzy

    private ClaudeActivity _claude;
    private double _claudeTime;

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

    /// <summary>Seconds without keyboard/mouse input before it naps.</summary>
    public double SleepAfterIdle { get; init; } = 90;

    public PetEffect Effect => _state switch
    {
        PetState.Sleep => PetEffect.Sleeping,
        PetState.Dizzy => PetEffect.Dizzy,
        PetState.Alert => PetEffect.Alert,
        PetState.Celebrate => PetEffect.Sparkles,
        PetState.Hop when _claude == ClaudeActivity.NeedsAttention => PetEffect.Alert,
        PetState.Hop when _claude == ClaudeActivity.Done => PetEffect.Sparkles,
        _ => PetEffect.None,
    };

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

    public void OnClaude(ClaudeActivity activity)
    {
        _claude = activity;
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

        switch (_state)
        {
            case PetState.Falling:
            case PetState.Hop:
            case PetState.Jump:
                TickAirborne(area, dt);
                break;

            case PetState.Held:
                break;

            default:
                if (!StandOnSurface(area)) { Enter(PetState.Falling); break; }
                TickGrounded(area, dt);
                break;
        }

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
                if (s.Id != _surface.Id) continue;
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
            case PetState.Land:
                if (_stateTime <= 0)
                {
                    if (_dizzyOnLanding) Enter(PetState.Dizzy, DizzyTime);
                    else Enter(PetState.Idle, 0.4);
                }
                return;
            case PetState.Dizzy:
            case PetState.Wake:
                if (_stateTime <= 0) Enter(PetState.Idle, 0.5);
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
            if (s.Id == _surface.Id) continue;
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

        _dizzyOnLanding = _thrown && _state == PetState.Falling && _vy > DizzySpeed * S;
        _thrown = false;
        _surface = ground;
        _y = ground.Top - H;
        _vx = _vy = 0;
        Enter(PetState.Land, LandTime);
    }

    private static Surface Floor(WorkArea area) => new(0, area.Left, area.Right, area.Bottom, 0, 0);

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
            _thrown = true;
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
            PetState.Falling => Anim.Fall,
            PetState.Jump => Anim.Jump,
            PetState.Land => Anim.Land,
            PetState.Dizzy => Anim.Dizzy,
            PetState.Wake => Anim.Wake,
            PetState.Working => Anim.Working,
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
