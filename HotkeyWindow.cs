using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// RegisterHotKey needs an HWND with a message loop to post WM_HOTKEY to. This is a hidden,
/// never-shown tool window that exists solely to receive that message.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xB1F; // arbitrary; only needs to be unique within this window

    private Keys _registered = Keys.None;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public HotkeyWindow()
    {
        // WS_EX_TOOLWINDOW keeps it out of the taskbar and Alt+Tab even though it is a
        // top-level window; it is never given WS_VISIBLE, so nothing is ever drawn.
        CreateHandle(new CreateParams
        {
            Caption = "FullscreenToggleHotkeyWindow",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,
            ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Parent = IntPtr.Zero
        });
    }

    public bool IsRegistered => _registered != Keys.None;

    /// <summary>
    /// Claims <paramref name="hotkey"/>, replacing whatever was registered before. On failure
    /// the previous binding is put back, so a rejected change never leaves the app deaf.
    /// Returns null on success, or a human-readable Win32 message on failure.
    /// </summary>
    public string? TryRegister(Keys hotkey)
    {
        if (!AppSettings.IsValidHotkey(hotkey))
            return "That key combination is not usable as a shortcut.";

        Keys previous = _registered;
        Unregister();

        if (TryRegisterCore(hotkey, out string? error))
        {
            _registered = hotkey;
            return null;
        }

        if (previous != Keys.None && TryRegisterCore(previous, out _))
            _registered = previous;

        return error;
    }

    private bool TryRegisterCore(Keys hotkey, out string? error)
    {
        bool ok = NativeMethods.RegisterHotKey(
            Handle,
            HotkeyId,
            AppSettings.ToWin32Modifiers(hotkey),
            AppSettings.ToVirtualKey(hotkey));

        // Read the error immediately - any intervening P/Invoke would overwrite it.
        error = ok ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return ok;
    }

    private void Unregister()
    {
        if (_registered == Keys.None || Handle == IntPtr.Zero)
            return;

        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registered = Keys.None;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
        DestroyHandle();
    }
}
