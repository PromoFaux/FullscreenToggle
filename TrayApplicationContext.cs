using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// Owns the tray icon, the settings dialog and the hidden hotkey window. There is no visible
/// form and no taskbar entry unless the user opens Settings.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly ToolStripMenuItem _hotkeyHint;

    private SettingsForm? _openSettingsForm;
    private bool _cleanedUp;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += (_, _) => WindowToggler.ToggleForegroundWindow();

        // Shows the current shortcut; not clickable, because toggling from the menu would
        // read the menu's own owner window as "foreground", which is never what was meant.
        _hotkeyHint = new ToolStripMenuItem { Enabled = false };

        _trayIcon = new NotifyIcon
        {
            Icon = AppIcon.LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        RefreshHotkeyLabels();
    }

    #region Startup

    /// <summary>
    /// Claims the saved hotkey. If it's unavailable the user is offered the settings dialog
    /// rather than just being shown an error and dropped.
    /// </summary>
    public bool Initialize()
    {
        // If the exe has been moved since startup was enabled, re-point the Run key at
        // wherever it lives now. Cheap, silent, and saves a baffling "it stopped starting".
        StartupRegistration.RepairPathIfEnabled();

        // Make the config file visible next to a portable copy on its very first run.
        _settings.EnsureSaved();

        string? error = _hotkeyWindow.TryRegister(_settings.Hotkey);
        if (error is null)
            return true;

        DialogResult choice = MessageBox.Show(
            $"The shortcut {AppSettings.Describe(_settings.Hotkey)} could not be registered.\r\n\r\n" +
            $"{error}\r\n\r\nAnother application is probably already using it. " +
            "Would you like to choose a different shortcut now?",
            "FullscreenToggle",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (choice == DialogResult.No)
            return false;

        ShowSettings();

        // Still nothing registered means the user cancelled out; there's no point staying up.
        return _hotkeyWindow.IsRegistered;
    }

    #endregion

    #region Tray menu

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings())
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) // default action
        };

        menu.Items.Add(_hotkeyHint);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        return menu;
    }

    private void RefreshHotkeyLabels()
    {
        string shortcut = AppSettings.Describe(_settings.Hotkey);

        _hotkeyHint.Text = $"Toggle active window:  {shortcut}";

        // NotifyIcon.Text is capped at 63 characters; an exotic key name could reach it.
        string tooltip = $"FullscreenToggle ({shortcut})";
        _trayIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    #endregion

    #region Settings dialog

    private void ShowSettings()
    {
        // Re-entrant via double-click plus menu; keep it to one dialog.
        if (_openSettingsForm is not null)
        {
            _openSettingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(_settings, _hotkeyWindow.TryRegister);
        _openSettingsForm = form;

        try
        {
            form.ShowDialog();
        }
        finally
        {
            _openSettingsForm = null;
        }

        RefreshHotkeyLabels();
    }

    #endregion

    #region Shutdown

    protected override void ExitThreadCore()
    {
        Cleanup();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Cleanup();

        base.Dispose(disposing);
    }

    /// <summary>Idempotent - reached from both the Exit menu item and Main's using block.</summary>
    private void Cleanup()
    {
        if (_cleanedUp)
            return;

        _cleanedUp = true;

        _trayIcon.Visible = false; // otherwise a ghost icon lingers until the user hovers it
        _trayIcon.Icon?.Dispose();
        _trayIcon.Dispose();
        _hotkeyWindow.Dispose();
    }

    #endregion
}
