; ============================================================================
; BornoKey (বর্ণকী) — Ridmik-Style Bengali Virtual Keyboard for Windows
; Inno Setup 6.x Production Installer Script (x64)
; Security Hardening: CHK-08 (%ProgramFiles%\BornoKey ACLs) & CHK-09 (Quoted Run Key)
; ============================================================================

#define MyAppName "BornoKey"
#define MyAppDisplayName "BornoKey — Bengali Virtual Keyboard"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "BornoKey Open Source Project"
#define MyAppURL "https://github.com/shantopaul/BornoKey"
#define MyAppExeName "BornoKey.exe"
#define MyPublishDir "..\publish\win-x64"

[Setup]
AppId={{8F6D32A1-5C7E-4D19-9E42-7B3A9C01E845}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=BornoKey-Setup-v{#MyAppVersion}-win-x64
SetupIconFile=..\assets\Icons\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
#ifdef SignTool
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start {#MyAppName} automatically when Windows starts"; GroupDescription: "Startup Options:"

[Files]
; Core published application binaries, JSON layout definitions, offline dictionary seed, and emoji catalog
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

; Optional native C++ Windows Text Services Framework (TSF) COM DLL (registered only in Administrator-protected {app} per CHK-08)
Source: "{#MyPublishDir}\BengaliKeyboard.TSF.dll"; DestDir: "{app}"; Flags: ignoreversion regserver noregerror skipifsourcedoesntexist 64bit

[Icons]
Name: "{autoprograms}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\Icons\app-icon.ico"
Name: "{autodesktop}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\Icons\app-icon.ico"; Tasks: desktopicon

[Registry]
; Quoted executable path in HKCU Run prevents unquoted path hijacking (CHK-09)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BornoKey"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  LocalAppDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    LocalAppDataPath := ExpandConstant('{localappdata}\BengaliKeyboard');
    if DirExists(LocalAppDataPath) then
    begin
      if MsgBox('Do you also want to remove your local BornoKey settings, emoji usage history, and encrypted clipboard database from ' + LocalAppDataPath + '?',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(LocalAppDataPath, True, True, True);
      end;
    end;
  end;
end;
