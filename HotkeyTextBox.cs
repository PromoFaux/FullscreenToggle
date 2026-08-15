using System.ComponentModel;
using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// A read-only TextBox that captures a key combination instead of accepting text.
/// Focus it and press e.g. Ctrl+Alt+F; the combination is shown and stored in <see cref="Hotkey"/>.
/// </summary>
internal sealed class HotkeyTextBox : TextBox
{
    private const string EmptyPrompt = "Click here, then press a key combination";

    private Keys _hotkey;

    /// <summary>
    /// Raised only when the user completes a combination, not when Hotkey is assigned in
    /// code - so reverting a rejected shortcut can't loop back into the apply logic.
    /// </summary>
    public event EventHandler? HotkeyCommitted;

    public HotkeyTextBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false; // no Ctrl+V etc. stealing our key presses
        TextAlign = HorizontalAlignment.Center;
        Cursor = Cursors.Default;
        Text = EmptyPrompt;
    }

    /// <summary>Set in code only - this control is never dropped on a designer surface.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            RefreshText();
        }
    }

    /// <summary>
    /// ProcessCmdKey rather than OnKeyDown: Alt- and Tab-based combinations are handled as
    /// command keys and never reach the normal key events, so this is the only hook that
    /// sees every combination the user might press.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;

        // Modifiers on their own: show the in-progress combination and keep waiting.
        if (key == Keys.None || AppSettings.IsModifierKey(key))
        {
            SetText(modifiers == Keys.None
                ? EmptyPrompt
                : AppSettings.Describe(modifiers) + " + …");
            return true;
        }

        // No modifier held - let it through so Tab, Enter and Escape still drive the dialog.
        if (modifiers == Keys.None)
            return base.ProcessCmdKey(ref msg, keyData);

        Hotkey = modifiers | key;
        HotkeyCommitted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Discards a half-typed combination ("Ctrl + …") when focus moves away.</summary>
    protected override void OnLostFocus(EventArgs e)
    {
        RefreshText();
        base.OnLostFocus(e);
    }

    private void RefreshText() =>
        SetText(AppSettings.IsValidHotkey(_hotkey) ? AppSettings.Describe(_hotkey) : EmptyPrompt);

    /// <summary>
    /// Assigning Text leaves the whole string selected while the box has focus, which looks
    /// like editable selected text in a control that isn't editable. Collapse it to the end.
    /// </summary>
    private void SetText(string value)
    {
        Text = value;
        Select(value.Length, 0);
    }
}
