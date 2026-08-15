using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace FullscreenToggle;

/// <summary>
/// User settings, persisted to %APPDATA%\FullscreenToggle\FullscreenToggle.settings.json.
/// The "start with Windows" flag is deliberately NOT stored here - the registry Run key
/// is the single source of truth for that, see <see cref="StartupRegistration"/>.
/// </summary>
internal sealed class AppSettings
{
    /// <summary>Ctrl+Alt+F is the default toggle shortcut.</summary>
    public const Keys DefaultHotkey = Keys.Control | Keys.Alt | Keys.F;

    /// <summary>
    /// Combined modifiers + key. Stored in JSON as readable text ("Ctrl + Alt + F") rather
    /// than the raw enum value, so the file can be hand-edited.
    /// </summary>
    [JsonConverter(typeof(HotkeyJsonConverter))]
    public Keys Hotkey { get; set; } = DefaultHotkey;

    #region Load / save

    private const string FileName = "FullscreenToggle.settings.json";

    /// <summary>
    /// The relaxed encoder stops '+' being written as "+" - this file is meant to be
    /// readable and hand-editable, and it is never embedded in HTML. TypeInfoResolver keeps
    /// serialization on the source-generated (reflection-free) path.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = AppSettingsJsonContext.Default
    };

    /// <summary>
    /// Resolved once at startup. Portable first: if the settings file can live next to the exe,
    /// it does, so dropping the binary in C:\bin keeps everything in one folder. Read-only
    /// locations (Program Files, a network share) fall back to %APPDATA%.
    /// </summary>
    [JsonIgnore]
    public static string FilePath { get; } = ResolveFilePath();

    /// <summary>True when settings sit beside the executable rather than in %APPDATA%.</summary>
    [JsonIgnore]
    public static bool IsPortable { get; private set; }

    private static string ResolveFilePath()
    {
        string? exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);

        if (!string.IsNullOrEmpty(exeDirectory))
        {
            string portablePath = Path.Combine(exeDirectory, FileName);

            // An existing file wins outright; otherwise only claim the spot if it's writable.
            if (File.Exists(portablePath) || IsDirectoryWritable(exeDirectory))
            {
                IsPortable = true;
                return portablePath;
            }
        }

        IsPortable = false;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FullscreenToggle",
            FileName);
    }

    /// <summary>Probe with a real file - directory ACLs are too subtle to infer any other way.</summary>
    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".fstoggle-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Never throws - a missing or corrupt file just means defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            string json = File.ReadAllText(FilePath);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

            if (loaded is null || !IsValidHotkey(loaded.Hotkey))
                return new AppSettings();

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes the file if it doesn't exist yet. A freshly deployed portable copy then shows
    /// the settings file next to the exe straight away, rather than leaving the user guessing
    /// where configuration lives. Silent on failure - a read-only folder is not an error.
    /// </summary>
    public void EnsureSaved()
    {
        if (!File.Exists(FilePath))
            TrySave();
    }

    /// <summary>Returns an error message on failure, or null on success.</summary>
    public string? TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    #endregion

    #region Hotkey conversion and formatting

    /// <summary>A usable hotkey needs at least one modifier plus one non-modifier key.</summary>
    public static bool IsValidHotkey(Keys hotkey)
    {
        Keys key = hotkey & Keys.KeyCode;
        Keys modifiers = hotkey & Keys.Modifiers;

        return modifiers != Keys.None && key != Keys.None && !IsModifierKey(key);
    }

    /// <summary>True for the standalone modifier keycodes, which can't be a hotkey's trigger key.</summary>
    public static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;

    /// <summary>
    /// Modifier flags for RegisterHotKey. MOD_NOREPEAT is always added so holding the
    /// combination fires once rather than repeating. The Windows key is not offered -
    /// a TextBox can't reliably capture it and most Win+key combos are OS-reserved.
    /// </summary>
    public static uint ToWin32Modifiers(Keys hotkey)
    {
        uint modifiers = NativeMethods.MOD_NOREPEAT;

        if ((hotkey & Keys.Control) == Keys.Control) modifiers |= NativeMethods.MOD_CONTROL;
        if ((hotkey & Keys.Alt) == Keys.Alt) modifiers |= NativeMethods.MOD_ALT;
        if ((hotkey & Keys.Shift) == Keys.Shift) modifiers |= NativeMethods.MOD_SHIFT;

        return modifiers;
    }

    public static uint ToVirtualKey(Keys hotkey) => (uint)(hotkey & Keys.KeyCode);

    /// <summary>Renders a hotkey the way Windows menus do: "Ctrl + Alt + F".</summary>
    public static string Describe(Keys hotkey)
    {
        Keys key = hotkey & Keys.KeyCode;
        var parts = new List<string>(4);

        if ((hotkey & Keys.Control) == Keys.Control) parts.Add("Ctrl");
        if ((hotkey & Keys.Alt) == Keys.Alt) parts.Add("Alt");
        if ((hotkey & Keys.Shift) == Keys.Shift) parts.Add("Shift");

        if (key != Keys.None && !IsModifierKey(key))
            parts.Add(DescribeKey(key));

        return parts.Count == 0 ? "(none)" : string.Join(" + ", parts);
    }

    /// <summary>
    /// Friendly names for keys whose enum name is unhelpful ("Oem3", "D1"). Used in both
    /// directions, so Describe and TryParse can never drift apart.
    /// </summary>
    private static readonly (Keys Key, string Name)[] SpecialKeyNames =
    [
        (Keys.Oemtilde, "`"),
        (Keys.OemMinus, "-"),
        (Keys.Oemplus, "="),
        (Keys.OemOpenBrackets, "["),
        (Keys.OemCloseBrackets, "]"),
        (Keys.OemSemicolon, ";"),
        (Keys.OemQuotes, "'"),
        (Keys.Oemcomma, ","),
        (Keys.OemPeriod, "."),
        (Keys.OemQuestion, "/"),
        (Keys.OemPipe, "\\"),
        (Keys.Back, "Backspace"),
        (Keys.Capital, "Caps Lock"),
        (Keys.Next, "Page Down"),
        (Keys.Prior, "Page Up"),
        (Keys.Return, "Enter")
    ];

    /// <summary>Keys.ToString() produces "D1" and "Oem3"; this is the readable form.</summary>
    private static string DescribeKey(Keys key)
    {
        foreach ((Keys candidate, string name) in SpecialKeyNames)
        {
            if (candidate == key)
                return name;
        }

        return key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (char)('0' + (key - Keys.NumPad0)),
            _ => key.ToString()
        };
    }

    /// <summary>
    /// Inverse of <see cref="Describe"/>, tolerant of hand-editing: case-insensitive,
    /// whitespace-insensitive, and accepts "Control" for "Ctrl".
    /// </summary>
    public static bool TryParse(string? text, out Keys hotkey)
    {
        hotkey = Keys.None;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (string rawToken in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
                continue;

            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    hotkey |= Keys.Control;
                    continue;
                case "alt":
                    hotkey |= Keys.Alt;
                    continue;
                case "shift":
                    hotkey |= Keys.Shift;
                    continue;
            }

            if (!TryParseKey(token, out Keys key))
                return false;

            // Two trigger keys in one string is a malformed entry, not a hotkey.
            if ((hotkey & Keys.KeyCode) != Keys.None)
                return false;

            hotkey |= key;
        }

        return IsValidHotkey(hotkey);
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        foreach ((Keys candidate, string name) in SpecialKeyNames)
        {
            if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }

        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            key = Keys.D0 + (token[0] - '0');
            return true;
        }

        if (token.StartsWith("num ", StringComparison.OrdinalIgnoreCase) &&
            token.Length == 5 && token[4] is >= '0' and <= '9')
        {
            key = Keys.NumPad0 + (token[4] - '0');
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && Enum.IsDefined(key);
    }

    #endregion
}

/// <summary>
/// Writes the hotkey as "Ctrl + Alt + F" instead of an opaque enum number, so the settings file
/// is worth opening. Still reads the old numeric form, and falls back to the default rather
/// than throwing if someone hand-edits it into nonsense.
/// </summary>
internal sealed class HotkeyJsonConverter : JsonConverter<Keys>
{
    public override Keys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return (Keys)reader.GetInt32(); // legacy numeric form

        return AppSettings.TryParse(reader.GetString(), out Keys hotkey)
            ? hotkey
            : AppSettings.DefaultHotkey;
    }

    public override void Write(Utf8JsonWriter writer, Keys value, JsonSerializerOptions options) =>
        writer.WriteStringValue(AppSettings.Describe(value));
}

/// <summary>
/// Source-generated JSON so serialization stays reflection-free (and therefore safe if
/// trimming or AOT is ever turned on).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
