using System;
using System.Diagnostics;

namespace DesktopClawd;

/// <summary>A monitor's usable area (excluding the taskbar), in physical screen pixels.</summary>
public readonly record struct WorkArea(double Left, double Top, double Right, double Bottom);

/// <summary>What the UI framework must provide to the brain.</summary>
public interface IPetHost
{
    /// <summary>Physical pixels per DIP for the monitor the pet is on.</summary>
    double Scale { get; }
    double PxWidth { get; }
    double PxHeight { get; }
    WorkArea WorkAreaAt(double x, double y);
}

public enum PetState { Idle, Walk, Sleep, Held, Falling, Hop, Land, Working, Alert, Celebrate }

/// <summary>What Claude Code is doing, as reported by its hooks.</summary>
public enum ClaudeActivity { None, Working, NeedsAttention, Done }

/// <summary>
/// Framework-independent behaviour and physics.
/// Positions and velocities are in physical screen pixels; tuning constants are in DIPs.
/// </summary>
public sealed class PetBrain(IPetHost host)
{
    private const double WalkSpeed = 45;    // DIP/s
    private const double Gravity = 1800;    // DIP/s^2
    private const double HopSpeed = 420;    // DIP/s
    private const double MaxThrow = 2500;   // DIP/s
    private const double DragThreshold = 4; // DIP
    private const double WallBounce = 0.5;  // fraction of speed kept when hitting a side or the top
    private const double LandTime = 0.12;   // s of squash after landing

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

    private double S => host.Scale;
    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Drop in from the top-centre of the given area.</summary>
    public void Start(WorkArea area)
    {
        _x = (area.Left + area.Right) / 2 - host.PxWidth / 2;
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

        var area = host.WorkAreaAt(_x + host.PxWidth / 2, _y + host.PxHeight / 2);
        var ground = area.Bottom - host.PxHeight;
        _stateTime -= dt;

        if (_claude != ClaudeActivity.None && (_claudeTime -= dt) <= 0)
            _claude = ClaudeActivity.None;

        switch (_state)
        {
            case PetState.Idle:
            case PetState.Walk:
            case PetState.Sleep:
            case PetState.Working:
            case PetState.Alert:
            case PetState.Celebrate:
                if (_y < ground - 1) { Enter(PetState.Falling); break; }
                _y = ground;
                TickGrounded(area);
                break;

            case PetState.Land:
                _y = ground;
                if (_stateTime <= 0) Enter(PetState.Idle, 0.4);
                break;

            case PetState.Falling:
            case PetState.Hop:
                _vy += Gravity * S * dt;
                _x += _vx * dt;
                _y += _vy * dt;
                if (_x < area.Left) { _x = area.Left; _vx = Math.Abs(_vx) * WallBounce; }
                if (_x + host.PxWidth > area.Right) { _x = area.Right - host.PxWidth; _vx = -Math.Abs(_vx) * WallBounce; }
                if (_y < area.Top) { _y = area.Top; _vy = Math.Abs(_vy) * WallBounce; }
                if (_y >= ground && _vy >= 0)
                {
                    _y = ground;
                    _vx = _vy = 0;
                    Enter(PetState.Land, LandTime);
                }
                break;

            case PetState.Held:
                break;
        }

        UpdateBlink(dt);
        RefreshAnimation();

        void TickGrounded(WorkArea area)
        {
            // Claude activity takes priority over wandering.
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

            if (_state == PetState.Walk)
            {
                _x += _vx * dt;
                if (_x < area.Left) { _x = area.Left; _vx = Math.Abs(_vx); }
                if (_x + host.PxWidth > area.Right) { _x = area.Right - host.PxWidth; _vx = -Math.Abs(_vx); }
                FacingLeft = _vx < 0;
                if (_stateTime <= 0) Enter(PetState.Idle, 1.5 + _rng.NextDouble() * 3);
                return;
            }

            if (_stateTime <= 0) ChooseNextAction();
        }
    }

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
            Enter(PetState.Falling);
        }
        else if (_state is not (PetState.Held or PetState.Falling or PetState.Hop))
        {
            // Clicking acknowledges a "needs attention" alert.
            if (_claude == ClaudeActivity.NeedsAttention) _claude = ClaudeActivity.None;
            StartHop(1);
        }
    }

    private void StartHop(double strength)
    {
        _vx = 0;
        _vy = -HopSpeed * strength * S;
        Enter(PetState.Hop);
    }

    private void ChooseNextAction()
    {
        var roll = _rng.NextDouble();
        if (_state == PetState.Sleep || roll < 0.35)
            Enter(PetState.Idle, 1.5 + _rng.NextDouble() * 3);
        else if (roll < 0.9)
            Enter(PetState.Walk, 2 + _rng.NextDouble() * 5);
        else
            Enter(PetState.Sleep, 10 + _rng.NextDouble() * 15);
    }

    private void Enter(PetState state, double duration = 0)
    {
        _state = state;
        _stateTime = duration;
        if (state == PetState.Walk)
            _vx = (_rng.Next(2) == 0 ? -1 : 1) * WalkSpeed * S;
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
            PetState.Land => Anim.Land,
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
