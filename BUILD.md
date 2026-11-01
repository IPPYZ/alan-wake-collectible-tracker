# Building Alan Wake Collectible Tracker 2.6.6 from source

Requirements
------------
- Windows 10 or 11 (x64)
- .NET Framework 4.8 Developer Pack (includes the reference assemblies)
  https://dotnet.microsoft.com/download/dotnet-framework/net48
- MSBuild (ships with the .NET Framework, Visual Studio, or Build Tools for Visual Studio)

No NuGet packages, no third-party libraries, no network access at build time.

Build
-----
From this folder:

    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe src2\AlanWakeTracker.csproj /p:Configuration=Release

The project's OutputPath is the repository root, so the build writes:

    AlanWakeTracker.exe

(If your MSBuild is 32-bit, use Framework\v4.0.30319 instead of Framework64.)

Run
---
The EXE expects an `images` folder next to it (the release ZIP ships it):

    AlanWakeTracker.exe
    images\aw\...      (298 location screenshots)
    images\an\...      (52 American Nightmare screenshots)

Without the images folder the app still runs; thumbnails are simply not shown.

What the program does
---------------------
- Reads Alan Wake (Steam 108710) and American Nightmare (202750) save files: `config`.
- Shows which collectibles are missing. The save is never modified.
- Writes backups to `Backups\` next to the EXE and settings to `%APPDATA%\AlanWakeTracker`.
- Opens no network connections, no telemetry, no updater.

Project layout
--------------
    src2\AlanWakeTracker.csproj   project file (WPF, .NET Framework 4.8)
    src2\Program.cs               entry point
    src2\Data.cs                  game/category/item model, localization, numbering
    src2\SaveParser.cs            save reading, backups, settings
    src2\SaveScanner.cs           save discovery (Steam, Epic, GOG, MS Store)
    src2\MainWindow.xaml(.cs)     the whole UI (views, overlays, icons)
    src2\Resources\data.json      324 Alan Wake items
    src2\Resources\an.json        53 American Nightmare pages
    src2\Resources\i18n.json      8 languages
    src2\AssemblyInfo.cs          version metadata (2.6.6.0)
    src2\app.manifest             DPI awareness / Windows compatibility
    src2\icon.ico                 application icon

License
-------
MIT for the code and data. Game names © Remedy Entertainment.
