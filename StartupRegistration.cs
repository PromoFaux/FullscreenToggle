using Microsoft.Win32;

namespace FullscreenToggle;

/// <summary>
/// "Start with Windows" via the per-user Run key. HKCU rather than HKLM so no elevation
/// is ever needed, and the value is re-pointed at the current exe each time it's enabled
/// (so moving the exe and re-ticking the box fixes a stale entry).
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FullscreenToggle";

    /// <summary>Path to the running executable, quoted so spaces survive the shell.</summary>
    private static string CurrentExecutableCommand
    {
        get
        {
            string? path = Environment.ProcessPath; // correct even for a single-file bundle
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Could not determine this application's executable path.");

            return $"\"{path}\"";
        }
    }

    public static bool IsEnabled() => GetRegisteredCommand() is not null;

    /// <summary>The command line currently registered, or null if startup is off.</summary>
    public static string? GetRegisteredCommand()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-points an existing startup entry at the running executable. Without this, moving
    /// the exe (say, from the publish folder to C:\bin) leaves the Run key aimed at a path
    /// that no longer exists and startup silently stops working. Called on every launch;
    /// does nothing when startup is disabled. Never throws.
    /// </summary>
    public static void RepairPathIfEnabled()
    {
        try
        {
            string? registered = GetRegisteredCommand();
            if (registered is null)
                return;

            if (!string.Equals(registered, CurrentExecutableCommand, StringComparison.OrdinalIgnoreCase))
                SetEnabled(true);
        }
        catch
        {
            // Best-effort self-heal; a failure here must never stop the app from starting.
        }
    }

    /// <summary>Throws on failure so the caller can surface a real message to the user.</summary>
    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($@"Could not open HKCU\{RunKeyPath}.");

        if (enabled)
            key.SetValue(ValueName, CurrentExecutableCommand, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
