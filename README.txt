S.C.S v2  -  Stratagem Control System
=====================================

BUILD (once)
  1. Install the .NET 8 SDK (x64): https://dotnet.microsoft.com/download/dotnet/8.0
  2. Double-click build.bat
  3. Run publish\SCS.exe  (single file, no Python, no installs needed to run it)

  Copy your old "stratagem_profiles" folder next to SCS.exe. v1.4 profiles load as-is,
  and profiles saved by v2 still open in v1.4.

FIRST RUN
  Settings > Lag Guard: set it to the lowest FPS you see in big fights (default 40).
  If you still see drops, lower it. If sequences feel slow and you never drop, raise it.
  RUN TIMING TEST shows how precisely this PC hits the target timings.

NOTES
  - Overlay and LED show over the game in Borderless / Windowed mode, not exclusive fullscreen.
  - If Helldivers 2 runs as administrator, run SCS.exe as administrator too.
  - Only one copy of SCS can run at a time (two would double-fire every stratagem).
