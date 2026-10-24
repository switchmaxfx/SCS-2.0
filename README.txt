S.C.S v2  -  Stratagem Control System
=====================================

RUN
  publish\SCS.exe is a single file. No install and no Python needed.
  Copy your old "stratagem_profiles" folder next to SCS.exe. v1.4 profiles load as-is,
  and profiles saved by v2 still open in v1.4.

BUILD (only if you want to build it yourself)
  1. Install the .NET 8 SDK (x64): https://dotnet.microsoft.com/download/dotnet/8.0
  2. Double-click build.bat
  3. Run publish\SCS.exe

FIRST RUN
  Settings > Lag Guard: set it to the lowest FPS you see in big fights (default 40).
  If you still see drops, lower it. If sequences feel slow and you never drop, raise it.
  RUN TIMING TEST shows how precisely this PC hits the target timings.

STRATAGEM CODES AND ICONS
  SCS keeps its stratagem list up to date from the Helldivers 2 wiki (helldivers.wiki.gg).
  At startup, at most once a day, it adds new stratagems, fixes changed codes, and
  downloads icons for stratagems added after this build. Results are saved next to the exe
  (stratagem_cache.json, stratagem_icons), so it keeps working offline.
  Turn this off or update by hand in Settings > Stratagem codes.

NETWORK
  The only site SCS contacts is helldivers.wiki.gg, to read public stratagem pages and icons.
  Nothing about you or your PC is sent. Arming and firing stratagems never use the network.
  wiki_sync.log next to the exe records what was downloaded.

NOTES
  - Overlay and LED show over the game in Borderless / Windowed mode, not exclusive fullscreen.
  - If Helldivers 2 runs as administrator, run SCS.exe as administrator too.
  - Only one copy of SCS can run at a time (two would double-fire every stratagem).
