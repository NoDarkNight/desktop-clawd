using System;
using System.Collections.Generic;

namespace DesktopClawd;

/// <summary>
/// A horizontal ledge the pet can stand on, in physical screen pixels: the visible part of a
/// window's top edge. <see cref="Id"/> identifies the window (0 = the screen floor);
/// <see cref="OwnerLeft"/>/<see cref="OwnerTop"/> are the window's origin when this was measured,
/// so the ledge can be shifted when the window moves.
/// </summary>
public readonly record struct Surface(nint Id, double Left, double Right, double Top, double OwnerLeft, double OwnerTop)
{
    public Surface Shift(double dx, double dy) =>
        this with { Left = Left + dx, Right = Right + dx, Top = Top + dy, OwnerLeft = OwnerLeft + dx, OwnerTop = OwnerTop + dy };
}

/// <summary>OS-specific desktop access. Everything is optional: unsupported platforms use <see cref="NullDesktop"/>.</summary>
public interface IDesktopEnvironment
{
    /// <summary>Visible top edges of other apps' windows, in physical pixels.</summary>
    IReadOnlyList<Surface> ScanSurfaces();

    /// <summary>Current origin of a window, or false if it's gone, hidden or minimised.</summary>
    bool TryGetWindowOrigin(nint id, out double left, out double top);

    /// <summary>Cursor position in physical pixels, if available.</summary>
    (double X, double Y)? CursorPosition { get; }

    /// <summary>Time since the last keyboard or mouse input anywhere, if available.</summary>
    TimeSpan? UserIdleTime { get; }

    /// <summary>Makes a window pass mouse input through to whatever is underneath.</summary>
    void SetClickThrough(nint window, bool clickThrough);
}

public sealed class NullDesktop : IDesktopEnvironment
{
    public IReadOnlyList<Surface> ScanSurfaces() => [];

    public bool TryGetWindowOrigin(nint id, out double left, out double top)
    {
        left = top = 0;
        return false;
    }

    public (double X, double Y)? CursorPosition => null;
    public TimeSpan? UserIdleTime => null;
    public void SetClickThrough(nint window, bool clickThrough) { }
}
