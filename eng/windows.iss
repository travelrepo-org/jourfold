; Release builds pass the version with ISCC /DAppVersion=x.y.z
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
[Setup]
AppId={{A4F1077F-857B-4CFA-8BCB-A12F80773B56}
AppName=Jourfold
AppVersion={#AppVersion}
AppPublisher=Jourfold contributors
UninstallDisplayName=Jourfold
UninstallDisplayIcon={app}\Jourfold.Desktop.exe
SetupIconFile=..\assets\branding\jourfold.ico
CloseApplications=yes
WizardStyle=modern
DefaultDirName={localappdata}\Programs\Jourfold
DefaultGroupName=Jourfold
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=jourfold-setup-win-x64
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
[Files]
Source: "..\artifacts\jourfold-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Icons]
Name: "{group}\Jourfold"; Filename: "{app}\Jourfold.Desktop.exe"
Name: "{autodesktop}\Jourfold"; Filename: "{app}\Jourfold.Desktop.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Jourfold.Desktop.exe"; Description: "Launch Jourfold"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Classes\jourfold"; ValueType: string; ValueData: "URL:Jourfold"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\jourfold"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\jourfold\shell\open\command"; ValueType: string; ValueData: """{app}\Jourfold.Desktop.exe"" ""%1"""
