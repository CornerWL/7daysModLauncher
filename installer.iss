; Inno Setup script for 7daysModLauncher
; Requires: iscc installer.iss  (https://jrsoftware.org/isinfo.php)
; Expects release_single/7daysModLauncher.exe built via build.bat

#define MyAppName "7 days Mod Launcher"
#define MyAppExeName "7daysModLauncher.exe"
#define MyAppVersion "1.1.1"

[Setup]
AppId={{7D7D7D7D-7DTD-4D4F-4453-4C41554E4348}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\7daysModLauncher
DefaultGroupName={#MyAppName}
OutputDir=installer_out
OutputBaseFilename=7daysModLauncher-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "release_single\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch now"; Flags: nowait postinstall skipifsilent
