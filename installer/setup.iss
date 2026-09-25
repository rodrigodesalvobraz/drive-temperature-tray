#define AppName "SSD Temperature Tray"
#define AppVersion "1.0.0"
[Setup]
AppId={{39AD3EF8-7F82-4789-9430-7E5028C606D5}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\SsdTemperatureTray
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=SsdTemperatureTray-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
UninstallDisplayIcon={app}\SsdTemperatureTray.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: startup; Description: "Start automatically when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "..\build\SsdTemperatureTray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\SsdTemperatureTray.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\SsdTemperatureTray.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Always record cleanup, including when startup is enabled later in the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SsdTemperatureTray"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SsdTemperatureTray"; ValueData: """{app}\SsdTemperatureTray.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\SsdTemperatureTray.exe"; Description: "Launch SSD Temperature Tray"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then MsgBox('Microsoft .NET Framework 4.8 or newer is required.', mbError, MB_OK);
end;
