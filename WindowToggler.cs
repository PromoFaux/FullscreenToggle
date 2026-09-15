using System.Drawing;
using System.Runtime.InteropServices;

namespace FullscreenToggle;

/// <summary>
/// Flips the foreground window between its normal framed state and a borderless,
/// always-on-top, centered 16:9 window.
/// </summary>
internal static class WindowToggler
{
    /// <summary>Target aspect ratio of the borderless window.</summary>
    private const int AspectWidth = 16;
    private const int AspectHeight = 9;

    /// <summary>Bounds + maximized flag captured before a window went borderless, keyed by HWND.</summary>
    private readonly record struct SavedWindowState(Rectangle Bounds, bool WasMaximized);

    /// <summary>In-memory only - nothing is persisted across restarts.</summary>
    private static readonly Dictionary<IntPtr, SavedWindowState> SavedStates = new();

    /// <summary>The styles the toggle strips on the way in and re-adds on the way out.</summary>
    private const int ManagedStyles =
        NativeMethods.WS_CAPTION |
        NativeMethods.WS_SYSMENU |
        NativeMethods.WS_THICKFRAME |
        NativeMethods.WS_MINIMIZEBOX |
        NativeMethods.WS_MAXIMIZEBOX;

    public static void ToggleForegroundWindow()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();

        if (!IsTogglableWindow(hwnd))
            return;

        int style = GetStyle(hwnd);

        // "Does it look like a normal framed window?" - note WS_MAXIMIZEBOX is deliberately
        // not part of this test.
        bool looksNormal =
            HasStyle(style, NativeMethods.WS_CAPTION) ||
            HasStyle(style, NativeMethods.WS_SYSMENU) ||
            HasStyle(style, NativeMethods.WS_THICKFRAME) ||
            HasStyle(style, NativeMethods.WS_MINIMIZEBOX);

        if (looksNormal)
            EnterFullscreen(hwnd, style);
        else
            ExitFullscreen(hwnd, style);
    }

    #region Enter / exit

    private static void EnterFullscreen(IntPtr hwnd, int style)
    {
        SaveWindowState(hwnd);

        SetStyle(hwnd, style & ~ManagedStyles);

        if (!TryGetMonitorBounds(hwnd, out Rectangle monitor))
            return;

        // Height fills the monitor; width is derived from it, and the result is centered
        // horizontally. On a monitor taller than 16:9 (e.g. 16:10) the computed width is
        // wider than the screen and the window intentionally overhangs both edges.
        int targetHeight = monitor.Height;
        int targetWidth = targetHeight * AspectWidth / AspectHeight;
        int x = monitor.X + (monitor.Width - targetWidth) / 2;
        int y = monitor.Y;

        // HWND_TOPMOST is what puts it above the taskbar. No SWP_FRAMECHANGED, matching the
        // reference script - some fullscreen-capable games treat that flag's forced
        // WM_NCCALCSIZE as a cue to renegotiate exclusive/flip-mode display state, which is
        // what causes a brief black screen on toggle for those apps.
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, targetWidth, targetHeight, 0);
    }

    private static void ExitFullscreen(IntPtr hwnd, int style)
    {
        bool hasSavedState = SavedStates.Remove(hwnd, out SavedWindowState saved);

        // Styles come back regardless of whether we have bounds to restore.
        SetStyle(hwnd, style | ManagedStyles);

        if (!hasSavedState)
            return;

        if (saved.WasMaximized)
        {
            // Drop topmost first - ShowWindow won't do it, and skipping this would leave
            // maximized windows permanently pinned above everything else.
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        }
        else
        {
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_NOTOPMOST,
                saved.Bounds.X, saved.Bounds.Y, saved.Bounds.Width, saved.Bounds.Height, 0);
        }
    }

    #endregion

    #region Saved state

    private static void SaveWindowState(IntPtr hwnd)
    {
        PruneDeadHandles();

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            return;

        SavedStates[hwnd] = new SavedWindowState(rect.ToRectangle(), IsMaximized(hwnd));
    }

    /// <summary>Windows get closed while we hold their handle; don't grow the dictionary forever.</summary>
    private static void PruneDeadHandles()
    {
        if (SavedStates.Count == 0)
            return;

        foreach (IntPtr handle in SavedStates.Keys.Where(h => !NativeMethods.IsWindow(h)).ToList())
            SavedStates.Remove(handle);
    }

    private static bool IsMaximized(IntPtr hwnd)
    {
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
        };

        if (NativeMethods.GetWindowPlacement(hwnd, ref placement))
            return placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED;

        // Fall back to the style bit if GetWindowPlacement is unavailable for this window.
        return HasStyle(GetStyle(hwnd), NativeMethods.WS_MAXIMIZE);
    }

    #endregion

    #region Helpers

    /// <summary>Skips the null case plus the desktop/shell windows, which must never be restyled.</summary>
    private static bool IsTogglableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (!NativeMethods.IsWindow(hwnd))
            return false;

        if (hwnd == NativeMethods.GetShellWindow() || hwnd == NativeMethods.GetDesktopWindow())
            return false;

        return true;
    }

    /// <summary>
    /// Compound styles such as WS_CAPTION (WS_BORDER | WS_DLGFRAME) only count as present
    /// when every bit is set, so this tests for the full mask rather than "any bit".
    /// </summary>
    private static bool HasStyle(int style, int flag) => (style & flag) == flag;

    private static int GetStyle(IntPtr hwnd) =>
        (int)(long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);

    private static void SetStyle(IntPtr hwnd, int style) =>
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

    /// <summary>Full bounds (including the taskbar strip) of the monitor the window sits on.</summary>
    private static bool TryGetMonitorBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        bounds = info.rcMonitor.ToRectangle();
        return true;
    }

    #endregion
}
