using System.Drawing;
using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// Loads the app icon out of the embedded multi-size icon.ico. Picking the size explicitly
/// matters: the tray wants the small-icon size for the current DPI, not a scaled 32px image.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "FullscreenToggle.icon.ico";

    /// <summary>Best-matching frame from the .ico for the requested size.</summary>
    public static Icon Load(Size size)
    {
        using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return new Icon(stream, size);
    }

    /// <summary>Tray icons should match SM_CXSMICON, which already accounts for DPI.</summary>
    public static Icon LoadTrayIcon() => Load(SystemInformation.SmallIconSize);

    /// <summary>Title-bar and Alt+Tab icon for the settings dialog.</summary>
    public static Icon LoadWindowIcon() => Load(new Size(32, 32));
}
