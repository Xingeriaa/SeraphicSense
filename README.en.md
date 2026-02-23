# SeraphicSense (English)
<p align="center">
  <a href="https://github.com/Xingeriaa/SeraphicSense/actions/workflows/dotnet.yml"><img src="https://img.shields.io/github/actions/workflow/status/Xingeriaa/SeraphicSense/dotnet.yml?branch=main&label=build" alt="Build Status" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/v/release/Xingeriaa/SeraphicSense?display_name=tag" alt="Latest Release" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/releases"><img src="https://img.shields.io/github/downloads/Xingeriaa/SeraphicSense/total" alt="Total Downloads" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Xingeriaa/SeraphicSense" alt="License" /></a>
  <a href="https://github.com/Xingeriaa/SeraphicSense/issues"><img src="https://img.shields.io/github/issues/Xingeriaa/SeraphicSense" alt="Issues" /></a>
</p>

<p align="center">
  <a href="README.md">Home</a>
  |
  <a href="README.vi.md">Tiếng Việt</a>
</p>

## Overview
SeraphicSense is a deterministic Windows guardian tool with one purpose:

1. Keep required files present in the observed folder.
2. Remove forbidden files from that folder.

It is implemented as a WPF desktop app with tray behavior and background monitoring.

## Core Behavior
The app monitors a configured folder and enforces the following rules:

- Required base name: `MatureData-WindowsClient`
- Required extensions: `.pak`, `.sig`, `.ucas`, `.utoc`
- Forbidden base name: `VNGLogo-WindowsClient` (any extension)

When a filesystem event occurs (create/delete/rename), the app waits for a configurable delay (default `2000` ms), then validates:

1. Missing required files are copied from the configured source folder.
2. Any forbidden file matching `VNGLogo-WindowsClient*` is deleted.

## Main Features
- Folder monitoring with `FileSystemWatcher`
- Configurable validation delay (default `2000 ms`)
- Retry logic for locked files (copy/delete)
- System tray integration (`Open`, `Start/Stop`, `Check Updates`, `Exit`)
- Start with Windows (HKCU Run)
- Start minimized to tray
- Single-instance startup guard (second launch activates existing instance)
- GitHub update check with two update modes:
  - Application update (installer-based)
  - Data-only update (download data assets only)

## Configuration
Configuration file location:

- `%AppData%\SeraphicSense\config.json`

Main fields:

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
- `GitHubRepository` (fixed to `https://github.com/Xingeriaa/SeraphicSense.git`)
- `LastAppliedDataReleaseTag`

## Update System
The update checker inspects the latest GitHub release and classifies update type:

### Application Update
Chosen when a release includes an installer asset (`.exe` or `.msi`) and is considered newer.

Behavior:

1. Download installer to `%TEMP%\SeraphicSense\updates\...`
2. Launch installer silently
3. Exit current app instance

### Data-Only Update
Chosen when release data assets are present without requiring full app reinstall.

Supported data assets:

- A zip archive with data naming (for example `BackupPaks.zip`, `MatureData.zip`, `data-*.zip`)
- Or direct files with extensions: `.pak`, `.sig`, `.ucas`, `.utoc`

Behavior:

1. Download and extract/copy files into `SourceFolderPath`
2. Save `LastAppliedDataReleaseTag`
3. Trigger validation to heal observed folder immediately

### Optional Explicit Release Marker
You can force classification using release notes body:

- `update-type: data` or `[update-type:data]`
- `update-type: app` or `[update-type:app]`

## Installation
### Option 1: Installer (Recommended)
Download the latest setup executable from:

- `https://github.com/Xingeriaa/SeraphicSense/releases`

Run installer as Administrator if required by your target folder permissions.

### Option 2: Portable Build
Use published `win-x64` artifacts and run `SeraphicSense.exe` directly.

## Build From Source
Requirements:

- .NET SDK 9.0+
- Windows OS (WPF target: `net9.0-windows`)

Build:

```powershell
dotnet restore
dotnet build -c Release
```

Publish self-contained:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Build Installer (Inno Setup)
The repository includes `installer/SeraphicSense.iss`.

Example command:

```powershell
ISCC installer\SeraphicSense.iss /DPublishDir="C:\path\to\publish"
```

## Runtime Notes
- If writes fail in protected folders, run as Administrator.
- Game updates may overwrite files or lock files temporarily.
- The app retries copy/delete operations to handle transient locks.

## Repository
- Main repository: `https://github.com/Xingeriaa/SeraphicSense.git`
- CI workflow: `.github/workflows/dotnet.yml`

## License
This project is licensed under the terms in `LICENSE`.
