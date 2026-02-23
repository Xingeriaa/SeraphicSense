param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "artifacts/installer"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PublishDir)) {
    throw "PublishDir does not exist: $PublishDir"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$publishFullPath = (Resolve-Path $PublishDir).Path
$outputFullPath = (Resolve-Path $OutputDir).Path

$isccCandidates = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 first."
}

$publishEscaped = $publishFullPath.Replace("\", "\\")
$outputEscaped = $outputFullPath.Replace("\", "\\")

$tempIss = Join-Path $env:TEMP ("SeraphicSense-" + [Guid]::NewGuid().ToString("N") + ".iss")

$issScript = @"
#define AppName "SeraphicSense"
#define AppVersion "$Version"
#define AppPublisher "SeraphicSense"
#define AppExeName "SeraphicSense.exe"
#define PublishDir "$publishEscaped"

[Setup]
AppId={{4A0A9B74-D8E4-4DF9-B45C-3A95B9B876C7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
OutputDir=$outputEscaped
OutputBaseFilename=SeraphicSense-Setup

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{userappdata}\SeraphicSense"
Name: "{userappdata}\SeraphicSense\BackupPaks"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
"@

Set-Content -Path $tempIss -Value $issScript -Encoding Ascii

try {
    & $isccPath $tempIss
}
finally {
    Remove-Item -Path $tempIss -Force -ErrorAction SilentlyContinue
}

$installerPath = Join-Path $outputFullPath "SeraphicSense-Setup.exe"
if (-not (Test-Path $installerPath)) {
    throw "Installer was not generated: $installerPath"
}

Write-Host "Installer generated at: $installerPath"
