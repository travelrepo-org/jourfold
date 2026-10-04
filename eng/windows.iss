; Build after `python eng/package.py --rid win-<arch>`:
;   ISCC /DAppVersion=<python eng/version.py> [/DArch=arm64] eng/windows.iss
#ifndef AppVersion
  #error Pass the version from Directory.Build.props: ISCC /DAppVersion=x.y.z (python eng/version.py prints it)
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#if Arch == "x64"
  #define Allowed "x64compatible"
#elif Arch == "arm64"
  #define Allowed "arm64"
#else
  #error Arch must be x64 or arm64
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
ArchitecturesAllowed={#Allowed}
ArchitecturesInstallIn64BitMode={#Allowed}
OutputDir=..\artifacts
OutputBaseFilename=jourfold-setup-win-{#Arch}
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
[Files]
Source: "..\artifacts\jourfold-win-{#Arch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
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
