[Setup]
AppName=Quick Translate
AppVersion=1.0.0
AppPublisher=Quick Translate contributors
DefaultDirName={autopf}\QuickTranslate
DefaultGroupName=Quick Translate
PrivilegesRequired=admin
SetupIconFile=..\src\QuickTranslate.App\Assets\app.ico
OutputDir=..\dist
OutputBaseFilename=QuickTranslate-Setup
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\QuickTranslate.exe

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Quick Translate"; Filename: "{app}\QuickTranslate.exe"
Name: "{autodesktop}\Quick Translate"; Filename: "{app}\QuickTranslate.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"

[Run]
; creates the highest-privilege launch task so the app never asks for UAC again
Filename: "{app}\QuickTranslate.exe"; Parameters: "--install-task"; Flags: runhidden waituntilterminated
Filename: "{app}\QuickTranslate.exe"; Description: "Launch Quick Translate"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\QuickTranslate.exe"; Parameters: "--remove-task"; Flags: runhidden; RunOnceId: "RemoveTask"

