# SCS 2.0: Stratagem Control System

A stratagem macro for **Helldivers 2**, rewritten from Python into C# / .NET 8 (WPF) so it doesn't drop inputs when the game lags.

Bind any key or mouse button to a stratagem. Press it in-game and SCS opens the stratagem menu and enters the arrow code for you.

## Why 2.0

v1.x (Python + pydirectinput) missed inputs during heavy fights. The main reason was that every arrow was pressed and released back-to-back with **0 ms** between them. Helldivers 2 checks the keyboard once per frame, so at low FPS a 0 ms tap that falls between two checks never registers.

What 2.0 changes:

- **Lag Guard.** Every press and every release is held for at least one full frame at your worst-case FPS, so the game can't skip it.
- **Precise timing.** It uses a high-resolution waitable timer plus a short spin, so timings land within about 0.1 ms. Python's `sleep` could overshoot by 15 ms or more under load.
- **Dedicated threads.** A high-priority input thread sees your key presses, a separate thread sends the arrows, and the UI runs on its own thread, so nothing waits on the UI.
- **Hardware scancodes** via `SendInput`, for the arrows and for the menu key.
- **One stratagem at a time.** Executions run one after another instead of overlapping, and holding a key down doesn't fire it repeatedly.
- **Keys always released.** Every key is guaranteed to be released, even if something fails mid-sequence.

## Features

- 12 slots with a searchable, categorised stratagem picker
- Stratagem icons in the picker and on every slot card
- Stratagem codes kept up to date from the Helldivers 2 wiki
- Profiles (v1.x profiles load as-is)
- Master arm/disarm toggle key
- Optional: hide trigger keys from the game, fire only while the game is focused, WASD mode
- In-game overlay and status LED (borderless / windowed mode)
- Timing self-test and live execution telemetry

## Download

Get the latest `SCS.exe` from the [Releases](https://github.com/switchmaxfx/SCS-2.0/releases) page. It is a single self-contained file for Windows x64. Each release exe is built by GitHub Actions from that release's source and comes with a SHA-256 checksum.

A prebuilt copy is also kept at `publish/SCS.exe` in this repo.

## Build

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
2. Run `build.bat`.
3. Run `publish\SCS.exe`. It's a single self-contained file, so .NET doesn't need to be installed on the PC that runs it.

GitHub Actions also builds `SCS.exe` on every push. Open the **Actions** tab, then the latest run, then **Artifacts**.

## Stratagem codes from the wiki

SCS keeps its stratagem list current using the [Helldivers 2 wiki](https://helldivers.wiki.gg). On startup it checks the wiki at most once a day, adds any new stratagems, and updates codes that changed. The result is saved to `stratagem_cache.json` next to the exe, so it keeps working offline. The built-in list is the fallback if the wiki can't be reached.

Your profiles stay valid when names change: if the wiki's name for a stratagem differs from SCS's (for example "A/AC-8 Autocannon Sentry" vs "Autocannon Sentry"), SCS keeps its own name and accepts the wiki name as an alias.

Icons come from the wiki too. SCS ships with the current set built in. For stratagems added later, the sync downloads the wiki's SVG icon (a couple of KB each) into `stratagem_icons` next to the exe, and SCS draws it itself. Each icon is downloaded once.

You can turn automatic checks off or run an update yourself in **Settings > Stratagem codes**.

The idea of reading codes from the wiki comes from [goncaloestrelado/HelldiversMacro](https://github.com/goncaloestrelado/HelldiversMacro).

### Editing the built-in list

The fallback list lives in `Core/Stratagems.cs`. Add a line to the right category:

```csharp
("New Stratagem Name", "DLURD"),
```

`U`/`D`/`L`/`R` are the arrows in order. Rebuild afterwards.

## Network use

SCS works fully offline. Arming and firing stratagems never use the network.

The only site SCS contacts is `helldivers.wiki.gg`, and only to read public stratagem pages and icons: about 3 requests at startup at most once a day, plus one small download per missing icon. Nothing about you or your PC is sent. Turn it off in **Settings > Stratagem codes**, and check `wiki_sync.log` next to the exe for a record of what was downloaded.

## Notes

- If Helldivers 2 runs as administrator, run SCS as administrator too.
- The overlay and LED only show over the game in borderless or windowed mode.
- Only one copy of SCS can run at a time, which prevents double inputs.
- Profiles from v1.x load as they are, and profiles saved in 2.0 still open in v1.4.
