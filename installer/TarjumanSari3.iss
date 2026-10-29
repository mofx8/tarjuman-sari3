#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
[Setup]
AppName=Tarjuman Sari3
AppVersion={#AppVersion}
AppPublisher=Tarjuman Sari3 contributors
DefaultDirName={autopf}\TarjumanSari3
DefaultGroupName=Tarjuman Sari3
PrivilegesRequired=admin
SetupIconFile=..\src\TarjumanSari3.App\Assets\app.ico
OutputDir=..\dist
OutputBaseFilename=TarjumanSari3-Setup
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\TarjumanSari3.exe

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Tarjuman Sari3"; Filename: "{app}\TarjumanSari3.exe"
Name: "{autodesktop}\Tarjuman Sari3"; Filename: "{app}\TarjumanSari3.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"

[Run]
; creates the highest-privilege launch task so the app never asks for UAC again
Filename: "{app}\TarjumanSari3.exe"; Parameters: "--install-task"; Flags: runhidden waituntilterminated
Filename: "{app}\TarjumanSari3.exe"; Description: "Launch Tarjuman Sari3"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\TarjumanSari3.exe"; Parameters: "--remove-task"; Flags: runhidden; RunOnceId: "RemoveTask"


