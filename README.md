# FullscreenToggle

[![Build](https://github.com/PromoFaux/FullscreenToggle/actions/workflows/build.yml/badge.svg)](https://github.com/PromoFaux/FullscreenToggle/actions/workflows/build.yml)

A tray-resident hotkey utility. Press a global hotkey and the active window flips between its
normal framed state and a borderless, always-on-top, monitor-height 16:9 window centered on
whichever monitor it's currently on. Press it again and the window goes back exactly where it
was.

No visible window and no taskbar entry — just a tray icon, with a right-click menu for
**Settings…** and **Exit**.

## Deploying

It's one file. Copy `FullscreenToggle.exe` to `C:\bin` (or anywhere), double-click it, done.
On first run it drops a `FullscreenToggle.settings.json` next to itself; those two files are the whole install.

There's nothing to uninstall: delete the two files. If you enabled "start with Windows",
untick it first, or delete the `FullscreenToggle` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Building

```bash
dotnet publish -c Release
```

Produces a **559 KB** single exe in `bin\Release\net10.0-windows\win-x64\publish\`. This is a
framework-dependent build: the target machine needs the **.NET Desktop Runtime** installed.

```bash
winget install Microsoft.DotNet.DesktopRuntime.10
```

`RollForward=LatestMajor` is set, so it will also run on a machine that has only .NET 11 or 12
— you are not pinned to exactly 10.

For a machine where you can't install anything:

```bash
dotnet publish -c Release -p:Standalone=true
```

That's **47 MB** and needs nothing at all. Both write to the same publish folder, so the last
one you ran is what's sitting there.

### Why 47 MB, and why there's no middle option

The standalone build embeds the entire .NET runtime and WinForms — that bundle *is* the
"standard .NET DLLs", and carrying them is precisely what removes the install requirement.
The usual way to shrink it is trimming, but the SDK refuses to trim Windows Forms
(`NETSDK1175`), and NativeAOT doesn't support WinForms either. So it's 559 KB with a runtime
prerequisite, or 47 MB without one. Nothing in between is available.

Requires the **.NET SDK 10** to build (verified against 10.0.400).

```bash
dotnet test
```

Runs [tests/FullscreenToggle.Tests](tests/FullscreenToggle.Tests), which covers the hotkey
text parsing/formatting and JSON round-tripping in [AppSettings.cs](AppSettings.cs) — the
logic most likely to break in a way that only shows up as a corrupted settings file.

## Releases

Pushing a tag matching `vX.Y.Z` runs
[release.yml](.github/workflows/release.yml): it builds both variants with that version baked
into the exe (`-p:Version=X.Y.Z`), zips them, and attaches both zips to a GitHub release
created from the tag. The tag is the only place a release's version lives — nothing in the
repo needs bumping beforehand.

## Settings

Right-click the tray icon → **Settings…** (or double-click the icon).

**Changes apply and save the instant you make them.** There is no OK button — closing the
window with the X or Escape is a perfectly normal thing to do after flipping a toggle, and an
earlier version of this dialog silently discarded your change when you did. A settings toggle
that quietly does nothing is worse than no toggle at all.

- **Shortcut** — click the box and press the combination you want. Hold Ctrl, Alt and/or
  Shift, then press a key. At least one modifier is required. If the combination is already
  owned by another app, the box reverts and the status line says so — your working shortcut
  is never replaced by a broken one.
- **Startup** — writes the exe path to `HKCU\...\Run` (per-user, never needs elevation). If
  you later move the exe, the app re-points that entry at its new location on next launch, so
  moving it to `C:\bin` doesn't quietly break startup.

### FullscreenToggle.settings.json

Stored next to the exe when that folder is writable, otherwise `%APPDATA%\FullscreenToggle\`.
The status line at the bottom of the dialog always shows the resolved path.

```json
{
  "Hotkey": "Ctrl + Alt + F"
}
```

Hand-editable, and read back tolerantly — `ctrl+alt+f` and `CONTROL + SHIFT + F5` both parse.
Nonsense falls back to the default rather than crashing. Delete the file to reset.

The 16:9 ratio is a pair of constants at the top of [WindowToggler.cs](WindowToggler.cs).

## Files

| File | Role |
| --- | --- |
| [Program.cs](Program.cs) | Entry point, single-instance guard |
| [TrayApplicationContext.cs](TrayApplicationContext.cs) | Tray icon, menu, settings dialog lifetime |
| [WindowToggler.cs](WindowToggler.cs) | The window-toggle logic |
| [HotkeyWindow.cs](HotkeyWindow.cs) | Hidden window that receives `WM_HOTKEY` |
| [SettingsForm.cs](SettingsForm.cs) | The configuration dialog |
| [HotkeyTextBox.cs](HotkeyTextBox.cs) | Key-combination capture control |
| [AppSettings.cs](AppSettings.cs) | Persistence, portable path resolution, hotkey text format |
| [StartupRegistration.cs](StartupRegistration.cs) | The `Run` registry key |
| [AppIcon.cs](AppIcon.cs) | Loads the embedded icon at the right size |
| [NativeMethods.cs](NativeMethods.cs) | All user32 P/Invoke |

`icon.ico` carries nine sizes (16–256px). It's both the exe icon and an embedded resource, so
the tray and the dialog can request the size that matches the current DPI.

## Design notes

Saved window bounds live in memory, so restarting the app forgets them. A window that is
borderless at that point can still be toggled — it gets its styles back, it just stays where
it is.

- **Topmost is cleared when restoring a maximized window**, via an explicit `HWND_NOTOPMOST`
  call before `ShowWindow` — otherwise the window would stay pinned above everything else
  forever.
- **`SetWindowPos` is called with no flags (`0`)**, matching the reference script rather than
  passing `SWP_FRAMECHANGED`. That flag forces a `WM_NCCALCSIZE` non-client recalculation,
  which some fullscreen-capable games treat as a cue to renegotiate their display/swap-chain
  state — causing a brief black screen on toggle. Dropping it can leave frame remnants on a
  window that doesn't repaint its own border after a style change; none observed so far.
- **Dead handles are pruned** from the saved-state dictionary on each save, since a
  long-running tray app would otherwise accumulate entries for closed windows.
- **Per-monitor v2 DPI awareness** is enabled via `ApplicationHighDpiMode` in the .csproj.
  Without it Windows silently rescales the coordinates passed to `SetWindowPos` on high-DPI
  monitors and the window comes out the wrong size.

### Known behavior worth knowing about

On a monitor *taller* than 16:9 (1920×1200, 1600×1200, etc.) the computed width
(`height × 16 ÷ 9`) is wider than the screen, so the window overhangs both edges and the left
and right thirds are off-screen — an inherent limitation of deriving width from height. Fix by
clamping if you want it: take the smaller of `monitorHeight × 16 ÷ 9` and `monitorWidth`, then
derive the other dimension.

The Windows key is not offered as a modifier: a TextBox can't reliably capture it, and most
Win+key combinations are reserved by the OS.

Some windows refuse to be restyled — anything running elevated (unless this app is elevated
too), UWP/store apps, and games that reassert their own styles every frame. The toggle is a
no-op for those.

## License

[MIT](LICENSE)

---

*Inspired by a DisplayFusion "borderless 16:9" script; this is a standalone reimplementation
with no DisplayFusion dependency.*
