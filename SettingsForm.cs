using System.Drawing;
using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// The right-click "Settings…" dialog: pick the shortcut, choose whether to run at sign-in.
///
/// Every change applies and saves the moment it's made - there is no OK button. An earlier
/// version committed on OK, which meant ticking "start with Windows" and then closing the
/// window with the X silently threw the change away. A settings toggle that quietly does
/// nothing is worse than no toggle, so the commit step is gone.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    /// <summary>Asks the owner to actually claim the hotkey. Returns an error message, or null on success.</summary>
    private readonly Func<Keys, string?> _tryApplyHotkey;

    private readonly HotkeyTextBox _hotkeyBox;
    private readonly CheckBox _startupCheckBox;
    private readonly Label _statusLabel;
    private readonly ToolTip _statusTip;

    /// <summary>Guards the CheckedChanged handler while it reverts a failed toggle.</summary>
    private bool _suppressStartupEvent;

    public SettingsForm(AppSettings settings, Func<Keys, string?> tryApplyHotkey)
    {
        _settings = settings;
        _tryApplyHotkey = tryApplyHotkey;

        _hotkeyBox = new HotkeyTextBox
        {
            Location = new Point(16, 48),
            Size = new Size(344, 27),
            Hotkey = settings.Hotkey
        };
        _hotkeyBox.HotkeyCommitted += OnHotkeyCommitted;

        _startupCheckBox = new CheckBox
        {
            Text = "Start FullscreenToggle when I sign in to Windows",
            Location = new Point(16, 28),
            Size = new Size(344, 24),
            Checked = StartupRegistration.IsEnabled()
        };
        _startupCheckBox.CheckedChanged += OnStartupToggled;

        _statusLabel = new Label
        {
            Location = new Point(14, 208),
            Size = new Size(286, 38),
            ForeColor = SystemColors.GrayText,
            AutoSize = false,
            AutoEllipsis = true // long install paths get "…" rather than being chopped mid-word
        };

        // The status line is deliberately small; the tooltip carries the untruncated text.
        _statusTip = new ToolTip { AutomaticDelay = 300 };

        InitializeLayout();
        ShowIdleStatus();
    }

    #region Layout

    private void InitializeLayout()
    {
        // AutoScaleMode.Font + PerMonitorV2 keeps these coordinates correct at any DPI.
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7F, 15F);

        Text = "FullscreenToggle Settings";
        Icon = AppIcon.LoadWindowIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true; // the tray icon is tiny; make the open dialog easy to find again
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 254);

        var shortcutGroup = new GroupBox
        {
            Text = "Shortcut",
            Location = new Point(12, 12),
            Size = new Size(376, 116)
        };
        shortcutGroup.Controls.Add(new Label
        {
            Text = "Press the key combination that should toggle the active window:",
            Location = new Point(14, 24),
            Size = new Size(348, 20),
            AutoSize = false
        });
        shortcutGroup.Controls.Add(_hotkeyBox);
        shortcutGroup.Controls.Add(new Label
        {
            Text = "Hold Ctrl, Alt and/or Shift, then press a key.",
            Location = new Point(14, 84),
            Size = new Size(348, 20),
            ForeColor = SystemColors.GrayText,
            AutoSize = false
        });

        var startupGroup = new GroupBox
        {
            Text = "Startup",
            Location = new Point(12, 138),
            Size = new Size(376, 62)
        };
        startupGroup.Controls.Add(_startupCheckBox);

        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(308, 213),
            Size = new Size(80, 29),
            DialogResult = DialogResult.OK // nothing to cancel - changes are already saved
        };

        Controls.Add(shortcutGroup);
        Controls.Add(startupGroup);
        Controls.Add(_statusLabel);
        Controls.Add(closeButton);

        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    #endregion

    #region Applying changes

    private void OnHotkeyCommitted(object? sender, EventArgs e)
    {
        Keys chosen = _hotkeyBox.Hotkey;

        if (chosen == _settings.Hotkey)
        {
            ShowIdleStatus();
            return;
        }

        string? error = _tryApplyHotkey(chosen);
        if (error is not null)
        {
            // Put the working shortcut back; assigning Hotkey doesn't re-raise the event.
            _hotkeyBox.Hotkey = _settings.Hotkey;
            ShowError($"{AppSettings.Describe(chosen)} is already in use by another app. " +
                      $"Still using {AppSettings.Describe(_settings.Hotkey)}.");
            return;
        }

        _settings.Hotkey = chosen;
        SaveAndReport($"Shortcut set to {AppSettings.Describe(chosen)}.");
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        if (_suppressStartupEvent)
            return;

        bool wanted = _startupCheckBox.Checked;

        try
        {
            StartupRegistration.SetEnabled(wanted);
        }
        catch (Exception ex)
        {
            _suppressStartupEvent = true;
            _startupCheckBox.Checked = !wanted;
            _suppressStartupEvent = false;

            ShowError($"Could not update the Windows startup entry. {ex.Message}");
            return;
        }

        ShowStatus(wanted
            ? "Will start automatically when you sign in."
            : "Will no longer start automatically.");
    }

    private void SaveAndReport(string successMessage)
    {
        string? saveError = _settings.TrySave();

        if (saveError is not null)
            ShowError($"Applied for this session, but saving to {AppSettings.FilePath} failed. {saveError}");
        else
            ShowStatus(successMessage);
    }

    #endregion

    #region Status line

    /// <summary>Idle state doubles as a hint about where settings actually live.</summary>
    private void ShowIdleStatus() =>
        ShowStatus($"Settings file: {AppSettings.FilePath}" +
                   (AppSettings.IsPortable ? " (portable)" : string.Empty));

    private void ShowStatus(string message) => SetStatus(message, SystemColors.GrayText);

    private void ShowError(string message) => SetStatus(message, Color.FromArgb(192, 32, 32));

    private void SetStatus(string message, Color color)
    {
        _statusLabel.ForeColor = color;
        _statusLabel.Text = message;
        _statusTip.SetToolTip(_statusLabel, message);
    }

    #endregion
}
