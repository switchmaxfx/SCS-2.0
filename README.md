# SCS 2.0 — Stratagem Control System

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
- Profiles (v1.x profiles load as-is)
- Master arm/disarm toggle key
- Optional: hide trigger keys from the game, fire only while the game is focused, WASD mode
- In-game overlay and status LED (borderless / windowed mode)
- Timing self-test and live execution telemetry

## Download

A prebuilt `publish/SCS.exe` (Windows x64, single self-contained file) is included in this repo.

## Build

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
2. Run `build.bat`.
3. Run `publish\SCS.exe`. It's a single self-contained file, so .NET doesn't need to be installed on the PC that runs it.

GitHub Actions also builds `SCS.exe` on every push. Open the **Actions** tab, then the latest run, then **Artifacts**.

## Adding new stratagems

Edit `Core/Stratagems.cs` and add a line to the right category:

```csharp
("New Stratagem Name", "DLURD"),
```

`U`/`D`/`L`/`R` are the arrows in order. Rebuild afterwards.

## Notes

- If Helldivers 2 runs as administrator, run SCS as administrator too.
- The overlay and LED only show over the game in borderless or windowed mode.
