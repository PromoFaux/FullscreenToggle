using System.Windows.Forms;

namespace FullscreenToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // A second instance would only fight the first one over the same hotkey.
        using var singleInstance = new Mutex(true, @"Local\FullscreenToggle.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "FullscreenToggle is already running (look for its icon in the notification area).",
                "FullscreenToggle",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Applies visual styles, text rendering and PerMonitorV2 DPI mode from the
        // Application* properties in the .csproj.
        ApplicationConfiguration.Initialize();

        using var context = new TrayApplicationContext();

        // Hotkey registration happens outside the constructor so a failure can bail out
        // before Application.Run starts pumping messages (ExitThread is a no-op until then).
        if (!context.Initialize())
            return;

        Application.Run(context);
    }
}
