# 7daysModLauncher

A Windows desktop application built with .NET 8 (WPF) that helps manage and launch mods for the game **7 Days to Die**.

## Publishing

The application can be published in two ways:

1. **Framework‑dependent release** – produces a set of files in the `release` folder. The target machine must have the appropriate .NET runtime installed.
2. **Self‑contained single‑file executable** – produces a single `7daysModLauncher.exe` (plus minimal config files) in the `release_single` folder. This executable runs on any Windows x64 machine without requiring a pre‑installed .NET runtime.

### How to publish

```bash
# Framework‑dependent (default)
 dotnet publish -c Release -o release

# Self‑contained single‑file (recommended for distribution)
 dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o release_single
```

The resulting `release_single` folder contains:
- `7daysModLauncher.exe` – the main executable.
- `7daysModLauncher.runtimeconfig.json` – runtime configuration.
- `7daysModLauncher.deps.json` – dependency information (required for the single‑file host).

Copy the entire contents of the chosen folder to the target machine and run `7daysModLauncher.exe`.

## Configuration

If you need to customize settings, edit the `settings.json` file (located in the `../Тестовый/` folder in the repository) and place it alongside the executable.
