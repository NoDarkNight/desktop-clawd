using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DesktopClawd;

/// <summary>Win32 implementation of <see cref="IDesktopEnvironment"/>. Coordinates are physical pixels (per-monitor DPI aware).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktop : IDesktopEnvironment
{
    private const double MinSurfaceWidth = 40; // px; narrower visible ledges are ignored
    private static readonly HashSet<string> ShellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private readonly HashSet<nint> _layered = [];

    private record struct Candidate(nint Hwnd, RECT Rect, bool Standable);

    public IReadOnlyList<Surface> ScanSurfaces()
    {
        // EnumWindows walks top-level windows front to back, so earlier entries cover later ones.
        var windows = new List<Candidate>();
        EnumWindows((hwnd, _) =>
        {
            if (TryClassify(hwnd, out var candidate)) windows.Add(candidate);
            return true;
        }, 0);

        var surfaces = new List<Surface>();
        var segments = new List<(double L, double R)>();
        for (var i = 0; i < windows.Count; i++)
        {
            var (hwnd, rect, standable) = windows[i];
            if (!standable) continue;

            segments.Clear();
            segments.Add((rect.Left, rect.Right));
            for (var j = 0; j < i && segments.Count > 0; j++)
            {
                var above = windows[j].Rect;
                if (above.Top <= rect.Top && above.Bottom > rect.Top)
                    Subtract(segments, above.Left, above.Right);
            }

            foreach (var (l, r) in segments)
            {
                if (r - l >= MinSurfaceWidth)
                    surfaces.Add(new Surface(hwnd, l, r, rect.Top, rect.Left, rect.Top));
            }
        }
        return surfaces;
    }

    public bool TryGetWindowOrigin(nint id, out double left, out double top)
    {
        left = top = 0;
        if (!IsWindow(id) || !IsWindowVisible(id) || IsIconic(id) || IsCloaked(id) || !TryGetBounds(id, out var rect))
            return false;
        left = rect.Left;
        top = rect.Top;
        return true;
    }

    public (double X, double Y)? CursorPosition => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    public TimeSpan? UserIdleTime
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return null;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }
    }

    public void SetClickThrough(nint window, bool clickThrough)
    {
        // WS_EX_TRANSPARENT only passes clicks through on a layered window, so make it layered
        // (fully opaque, so rendering is unchanged) the first time.
        var style = GetWindowLongPtr(window, GWL_EXSTYLE);
        if (_layered.Add(window))
        {
            style |= WS_EX_LAYERED;
            SetWindowLongPtr(window, GWL_EXSTYLE, style);
            SetLayeredWindowAttributes(window, 0, 255, LWA_ALPHA);
        }
        style = clickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(window, GWL_EXSTYLE, style);
    }

    /// <summary>Debug listing of what the scan sees (class names only, no titles).</summary>
    public IEnumerable<string> Describe()
    {
        var considered = new List<Candidate>();
        EnumWindows((hwnd, _) =>
        {
            if (TryClassify(hwnd, out var c)) considered.Add(c);
            return true;
        }, 0);
        yield return $"User idle: {UserIdleTime?.TotalSeconds:0.0}s, cursor: {CursorPosition}";
        yield return $"{considered.Count} visible window(s), front to back:";
        foreach (var (hwnd, r, standable) in considered)
        {
            var why = standable ? "standable"
                : IsZoomed(hwnd) ? "maximised"
                : IsFullScreen(hwnd, r) ? "full screen"
                : GetWindowTextLength(hwnd) == 0 ? "untitled"
                : "too small / tool window";
            yield return $"  {ClassName(hwnd),-40} {r.Left,6},{r.Top,-6} {r.Right - r.Left,5}x{r.Bottom - r.Top,-5} {why}";
        }
        var surfaces = ScanSurfaces();
        yield return $"{surfaces.Count} visible ledge(s):";
        foreach (var s in surfaces)
            yield return $"  {ClassName(s.Id),-40} x {s.Left,6}..{s.Right,-6} y {s.Top,6}";
    }

    private bool TryClassify(nint hwnd, out Candidate candidate)
    {
        candidate = default;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcess) return false;

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TRANSPARENT) != 0) return false; // click-through overlays don't occlude
        if (ShellClasses.Contains(ClassName(hwnd))) return false;
        if (!TryGetBounds(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;

        var hasTitle = GetWindowTextLength(hwnd) > 0;
        var isTool = (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0;
        if (!hasTitle && isTool) return false; // tooltips, shadows and other decoration

        var standable = hasTitle && !isTool && !IsZoomed(hwnd) && !IsFullScreen(hwnd, rect)
                        && rect.Right - rect.Left >= 120 && rect.Bottom - rect.Top >= 60;
        candidate = new Candidate(hwnd, rect, standable);
        return true;
    }

    private static void Subtract(List<(double L, double R)> segments, double cutL, double cutR)
    {
        for (var k = segments.Count - 1; k >= 0; k--)
        {
            var (l, r) = segments[k];
            if (cutR <= l || cutL >= r) continue;
            segments.RemoveAt(k);
            if (cutL > l) segments.Add((l, cutL));
            if (cutR < r) segments.Add((cutR, r));
        }
    }

    private static bool IsFullScreen(nint hwnd, RECT rect)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    private static bool TryGetBounds(nint hwnd, out RECT rect) =>
        // Extended frame bounds exclude the invisible resize border and shadow.
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0
        || GetWindowRect(hwnd, out rect);

    private static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    // --- Win32 ---

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TRANSPARENT = 0x20;
    private const nint WS_EX_TOOLWINDOW = 0x80;
    private const nint WS_EX_APPWINDOW = 0x40000;
    private const nint WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);
}
