# SeraphicSense
<p align="center">
  <a href="https://github.com/Xingeriaa/SeraphicSense/actions/workflows/dotnet.yml"><img src="https://img.shields.io/github/actions/workflow/status/Xingeriaa/SeraphicSense/dotnet.yml?branch=main&label=build" alt="Build Status" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/v/release/Xingeriaa/SeraphicSense?display_name=tag" alt="Latest Release" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/downloads/Xingeriaa/SeraphicSense/total" alt="Total Downloads" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Xingeriaa/SeraphicSense" alt="License" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/issues"><img src="https://img.shields.io/github/issues/Xingeriaa/SeraphicSense" alt="Issues" /></a>
</p>

<p align="center">
  <strong>English</strong>
  |
  <a href="README.vi.md">Vietnamese</a>
</p>

## Overview
SeraphicSense is a focused Windows tray utility that monitors a target VALORANT folder and enforces deterministic file rules.

Main use case:
- Remove `VNGLogo-WindowsClient*` files (remove VNG logo assets).
- Restore `MatureData-WindowsClient.*` files (add mature content assets into VALORANT data files).

## Important Disclaimer
- This project is provided for **educational purpose** and personal research only.
- This software is **not affiliated with Riot Games** or VALORANT.
- Modifying game files may violate Riot terms, trigger anti-cheat actions, or cause account penalties.
- By using this project, you accept all risks.
- The authors and contributors are **not responsible for anything that happens to your VALORANT account**, system, data, or game installation.

## Core Behavior
The application monitors a configured observed folder and applies:

- Required base name: `MatureData-WindowsClient`
- Required extensions: `.pak`, `.sig`, `.ucas`, `.utoc`
- Forbidden base name: `VNGLogo-WindowsClient` (any extension)

When file changes are detected (create/delete/rename), it waits for a configurable delay (default `2000` ms) and then:

1. Copies missing required files from source folder to observed folder.
2. Deletes forbidden files matching `VNGLogo-WindowsClient*`.

## Features
- WPF desktop UI + tray icon
- Start/Stop monitoring
- Configurable validation delay
- Retry logic for locked files
- Start with Windows (HKCU Run)
- Start minimized to tray
- Single-instance protection
- GitHub update checks with two update types:
  - Application update (installer)
  - Data-only update (download/replace data assets only)

## Update Model
SeraphicSense checks the latest GitHub release and classifies it:

### Application Update
Used when release includes installer assets (`.exe` or `.msi`).

Flow:
1. Download installer to `%TEMP%\SeraphicSense\updates\...`
2. Run installer silently
3. Exit current process

### Data-Only Update
Used when release contains data assets (`.zip` or direct `.pak/.sig/.ucas/.utoc`).

Flow:
1. Download/extract data assets to configured `SourceFolderPath`
2. Save applied release tag
3. Trigger validation so observed folder is healed immediately

You can force release classification using release notes:
- `update-type: data` or `[update-type:data]`
- `update-type: app` or `[update-type:app]`

## Configuration
Default config path:
- `%AppData%\SeraphicSense\config.json`

Common fields:
- `ObservedFolderPath`
- `SourceFolderPath`
- `RequiredBaseName`
- `RequiredExtensions`
- `ForbiddenBaseName`
- `ValidationDelayMs`
- `StartWithWindows`
- `StartMinimized`
- `AutoStartMonitoring`
- `CheckUpdatesOnLaunch`
- `GitHubRepository` (fixed: `https://github.com/Xingeriaa/SeraphicSense.git`)

## Installation
Download latest release:
- `https://github.com/Xingeriaa/SeraphicSense/releases`

Run installer with Administrator privileges if your target folders require elevated permissions.

## Build from Source
Requirements:
- .NET SDK 9.0+
- Windows

Commands:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

Installer script:
- `installer/SeraphicSense.iss`

## Repository
- Main repo: `https://github.com/Xingeriaa/SeraphicSense.git`
- Vietnamese documentation: `README.vi.md`

## License
See `LICENSE`.
