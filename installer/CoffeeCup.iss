; Inno Setup script for CoffeeCup. Built by .github/workflows/build.yml:
;   ISCC.exe /DAppVersion=1.2.3 installer\CoffeeCup.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "CoffeeCup.exe"
#define BuildDir "..\src\CoffeeCup\bin\Release\net48"

[Setup]
AppId={{6F2B1C3E-8D4A-4E7B-9C51-2A0F3D7E9B64}
AppName=CoffeeCup
AppVersion={#AppVersion}
AppVerName=CoffeeCup {#AppVersion}
AppPublisher=droza123
AppPublisherURL=https://github.com/droza123/coffeecup
; Per-user install by default: no admin prompt. Users can still choose "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\CoffeeCup
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=CoffeeCup-Setup-{#AppVersion}
SetupIconFile=..\assets\CoffeeCup.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=CoffeeCup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
; CoffeeCup is closed explicitly with "--exit" below, so Restart Manager isn't needed.
CloseApplications=no

[Tasks]
Name: "startup"; Description: "Start CoffeeCup when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#BuildDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CoffeeCup"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\CoffeeCup"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app manages this value itself ("Start with Windows" in its menu); the installer seeds it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CoffeeCup"; ValueData: """{app}\{#AppExe}"""; Tasks: startup
; Always remove the startup entry and settings on uninstall, whoever created them.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CoffeeCup"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\CoffeeCup"; ValueType: none; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch CoffeeCup"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitCoffeeCup"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Close a running copy so its .exe can be replaced during an upgrade. }
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
