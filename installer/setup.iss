#define AppName "Drive Temperature Tray"
#define AppVersion "1.0.4"
[Setup]
AppId={{39AD3EF8-7F82-4789-9430-7E5028C606D5}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\DriveTemperatureTray
DefaultGroupName={#AppName}
UsePreviousGroup=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=DriveTemperatureTray-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
UninstallDisplayIcon={app}\DriveTemperatureTray.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: startup; Description: "Start automatically when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "..\build\DriveTemperatureTray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\DriveTemperatureTray.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\images\*.png"; DestDir: "{app}\docs\images"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\DriveTemperatureTray.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Replace the startup entry from releases before the product rename.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SsdTemperatureTray"; Flags: deletevalue
; Always record cleanup, including when startup is enabled later in the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DriveTemperatureTray"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DriveTemperatureTray"; ValueData: """{app}\DriveTemperatureTray.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DriveTemperatureTray.exe"; Description: "Launch Drive Temperature Tray"; Flags: nowait postinstall skipifsilent

[InstallDelete]
Type: files; Name: "{app}\SsdTemperatureTray.exe"
Type: files; Name: "{app}\SsdTemperatureTray.exe.config"
Type: files; Name: "{userprograms}\SSD Temperature Tray\SSD Temperature Tray.lnk"
Type: files; Name: "{userprograms}\SSD Temperature Tray\Uninstall SSD Temperature Tray.lnk"
Type: dirifempty; Name: "{userprograms}\SSD Temperature Tray"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then MsgBox('Microsoft .NET Framework 4.8 or newer is required.', mbError, MB_OK);
end;
