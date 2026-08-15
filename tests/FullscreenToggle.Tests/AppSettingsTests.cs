using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace FullscreenToggle.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData(Keys.Control | Keys.Alt | Keys.F, "Ctrl + Alt + F")]
    [InlineData(Keys.Control | Keys.Shift | Keys.F5, "Ctrl + Shift + F5")]
    [InlineData(Keys.Alt | Keys.D1, "Alt + 1")]
    [InlineData(Keys.Control | Keys.OemMinus, "Ctrl + -")]
    [InlineData(Keys.Control | Keys.NumPad5, "Ctrl + Num 5")]
    [InlineData(Keys.None, "(none)")]
    public void Describe_matches_expected_text(Keys hotkey, string expected) =>
        Assert.Equal(expected, AppSettings.Describe(hotkey));

    [Theory]
    [InlineData("Ctrl + Alt + F", Keys.Control | Keys.Alt | Keys.F)]
    [InlineData("ctrl+alt+f", Keys.Control | Keys.Alt | Keys.F)]
    [InlineData("CONTROL + SHIFT + F5", Keys.Control | Keys.Shift | Keys.F5)]
    [InlineData("Alt + 1", Keys.Alt | Keys.D1)]
    [InlineData("Ctrl + -", Keys.Control | Keys.OemMinus)]
    public void TryParse_accepts_hand_edited_variants(string text, Keys expected)
    {
        Assert.True(AppSettings.TryParse(text, out Keys hotkey));
        Assert.Equal(expected, hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F")] // no modifier
    [InlineData("Ctrl")] // no trigger key
    [InlineData("Ctrl + F + G")] // two trigger keys
    [InlineData("Ctrl + Nonsense")] // unrecognized token
    public void TryParse_rejects_malformed_input(string? text) =>
        Assert.False(AppSettings.TryParse(text, out _));

    [Theory]
    [InlineData(Keys.Control | Keys.Alt | Keys.F)]
    [InlineData(Keys.Shift | Keys.F5)]
    [InlineData(Keys.Alt | Keys.D9)]
    [InlineData(Keys.Control | Keys.OemQuestion)]
    public void Describe_and_TryParse_round_trip(Keys hotkey)
    {
        string text = AppSettings.Describe(hotkey);

        Assert.True(AppSettings.TryParse(text, out Keys parsed));
        Assert.Equal(hotkey, parsed);
    }

    [Theory]
    [InlineData(Keys.Control | Keys.F, true)]
    [InlineData(Keys.F, false)] // no modifier
    [InlineData(Keys.Control, false)] // no trigger key
    [InlineData(Keys.Control | Keys.ControlKey, false)] // trigger key is itself a modifier
    public void IsValidHotkey_requires_a_modifier_and_a_non_modifier_key(Keys hotkey, bool expected) =>
        Assert.Equal(expected, AppSettings.IsValidHotkey(hotkey));

    [Fact]
    public void ToWin32Modifiers_always_includes_no_repeat() =>
        Assert.Equal(NativeMethods.MOD_NOREPEAT, AppSettings.ToWin32Modifiers(Keys.None));

    [Fact]
    public void ToWin32Modifiers_maps_each_held_modifier()
    {
        uint modifiers = AppSettings.ToWin32Modifiers(Keys.Control | Keys.Alt | Keys.Shift | Keys.F);

        Assert.Equal(
            NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
            modifiers);
    }
}

public class HotkeyJsonConverterTests
{
    // Mirrors the options AppSettings itself serializes with, so the '+' characters come
    // through unescaped and the readable-text assertion below can match them literally.
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = AppSettingsJsonContext.Default
    };

    [Fact]
    public void Reads_readable_text_form()
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
            """{"Hotkey":"Ctrl + Alt + F"}""", Options)!;

        Assert.Equal(Keys.Control | Keys.Alt | Keys.F, settings.Hotkey);
    }

    [Fact]
    public void Reads_legacy_numeric_form()
    {
        int legacy = (int)(Keys.Control | Keys.Shift | Keys.F5);

        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
            $$"""{"Hotkey":{{legacy}}}""", Options)!;

        Assert.Equal(Keys.Control | Keys.Shift | Keys.F5, settings.Hotkey);
    }

    [Fact]
    public void Falls_back_to_default_on_unparsable_text()
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
            """{"Hotkey":"not a hotkey"}""", Options)!;

        Assert.Equal(AppSettings.DefaultHotkey, settings.Hotkey);
    }

    [Fact]
    public void Writes_readable_text_form()
    {
        var settings = new AppSettings { Hotkey = Keys.Control | Keys.Alt | Keys.F };

        string json = JsonSerializer.Serialize(settings, Options);

        Assert.Contains("\"Ctrl + Alt + F\"", json);
    }
}
