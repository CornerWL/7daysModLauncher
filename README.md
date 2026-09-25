# 7daysModLauncher

A Windows desktop application built with .NET 8 (WPF) for managing mods for 7 Days to Die.

<img width="1536" height="1024" alt="picture" src="https://github.com/user-attachments/assets/1f8422d8-f4a9-4680-bb65-5f090f3b7956" />

## Features

* Install mods from ZIP (file dialog + drag & drop, multi-mod archives supported)
* Enable / disable mods (toggle-switch), Enable All / Disable All
* Search by name / author / version
* Open mod folder, delete mod
* Profiles: save / apply / delete
* ▶ Play: launch the game directly from the launcher (7DaysToDie.exe / _EAC)
* Auto-detect game folder (Steam registry + libraryfolders.vdf) with exe validation
* Full ModInfo.xml parsing: Name, Author, Version (attr/element/value), Description
* Settings + profiles in `%LocalAppData%/7daysModLauncher` (auto-migration from exe folder)
* File logging in `%LocalAppData%/7daysModLauncher/Logs/launcher.log` + global exception handler

## Requirements

* Windows 10/11
* For `release_single`: nothing extra, .NET Runtime is bundled (true single file)
* For framework-dependent build: .NET 8 Desktop Runtime

## Quick start

```bat
build.bat
```

It runs: build → tests → self-contained publish into `release_single/`.

Manual commands:

```bash
dotnet build 7daysModLauncher.csproj -c Release
dotnet test 7daysModLauncher.Tests/7daysModLauncher.Tests.csproj -c Release
dotnet publish 7daysModLauncher.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release_single
```

Note: tests target `net10.0-windows` (need .NET 10 SDK to run them), the app itself targets `net8.0-windows`.

## Distribution

Distribute a single file:

```text
release_single/7daysModLauncher.exe  (~68 MB, no DLLs needed, no runtime needed)
```

## Windows 11 notes

* SmartScreen may warn on first launch (unsigned exe): “More info” → “Run anyway”.
* If it doesn't start: run from terminal to see the error, and check `%LocalAppData%/7daysModLauncher/Logs/launcher.log`.

## Configuration

Settings and profiles are stored in `%LocalAppData%/7daysModLauncher`
(`settings.json`, `Profiles/`). Old files next to the executable are migrated automatically.
Logs: `%LocalAppData%/7daysModLauncher/Logs/launcher.log`.

## Project layout

```text
Services/      ModService, SettingsService, ProfileService,
               GamePathHelper, GameLauncherService, AppLogger
Models/        ModItem, Profile, AppSettings
ViewModels/    MainViewModel
Views/         MainWindow, ProfileNameDialog
Themes/        DarkTheme
7daysModLauncher.Tests/  xunit tests (GamePathHelper, GameLauncherService)
build.bat      build + test + publish
```
