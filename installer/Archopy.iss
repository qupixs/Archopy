#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\Archopy-win-x64"
#endif

[Setup]
AppId=Archopy.Desktop
AppName=Archopy
AppVersion={#AppVersion}
AppPublisher=qupix
AppCopyright=Copyright (c) 2026 qupix
AppMutex=Archopy.Desktop
DefaultDirName={localappdata}\Programs\Archopy
DefaultGroupName=Archopy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Archopy.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=Archopy-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Archopy\Assets\Archopy.ico
WizardImageFile=Wizard.bmp
WizardSmallImageFile=Mark.bmp
CloseApplications=yes
RestartApplications=no
#ifdef SignInstaller
SignTool=archopy
SignedUninstaller=yes
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{autoprograms}\Archopy"; Filename: "{app}\Archopy.exe"
Name: "{autodesktop}\Archopy"; Filename: "{app}\Archopy.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Archopy"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Archopy.exe"; Description: "Open Archopy"; Flags: nowait postinstall skipifsilent

[Code]
function GetWindowLong(Wnd: HWND; Index: Integer): Integer;
  external 'GetWindowLongW@user32.dll stdcall';
function SetWindowLong(Wnd: HWND; Index, Value: Integer): Integer;
  external 'SetWindowLongW@user32.dll stdcall';
function SetLayeredWindowAttributes(Wnd: HWND; Key: Cardinal; Alpha: Byte; Flags: Cardinal): Boolean;
  external 'SetLayeredWindowAttributes@user32.dll stdcall';

procedure InitializeWizard;
begin
  WizardForm.Color := clWhite;
  WizardForm.MainPanel.Color := clWhite;
  SetWindowLong(WizardForm.Handle, -20, GetWindowLong(WizardForm.Handle, -20) or $80000);
  SetLayeredWindowAttributes(WizardForm.Handle, 0, 232, 2);
end;
