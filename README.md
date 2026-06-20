# 7daysModLauncher

A Windows desktop application built with .NET 8 (WPF) for managing mods for 7 Days to Die.

<img width="1536" height="1024" alt="picture" src="https://github.com/user-attachments/assets/1f8422d8-f4a9-4680-bb65-5f090f3b7956" />




## Features

* Install mods
* Enable and disable mods
* Manage installed mods
* Launch the game directly from the launcher

## Requirements

* Windows 10/11
* .NET 8 Runtime (framework-dependent version only)

## Publishing

### Framework-Dependent Build

Requires .NET 8 Runtime to be installed on the target machine.

```bash
dotnet publish -c Release -o release
```

### Self-Contained Single-File Build

Recommended for distribution. Does not require .NET Runtime.

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o release_single
```

## Distribution

The published executable will be located in:

```text
release_single/7daysModLauncher.exe
```

Distribute the contents of the `release_single` folder to end users.

## Configuration

If configuration is required, place `settings.json` next to the executable.
