; Inno Setup 6 script for Vigil.
;
; Built by build/build.ps1 (and the release workflow):
;   ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<folder> installer\Vigil.iss
;
; Design notes
; - Per-user install (no administrator rights needed): %LOCALAPPDATA%\Programs\Vigil.
; - No drivers, services or scheduled tasks are installed. Vigil itself can create an optional
;   "Vigil (elevated startup)" task when the user turns that on in Settings; uninstall removes it if it can.
; - In-app updates run this installer with /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1
;   after Vigil has exited; /UPDATE=1 relaunches Vigil when the update finishes.

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif
#ifndef RepositoryUrl
  #define RepositoryUrl "https://github.com"
#endif

#define AppName "Vigil"
#define AppExe "Vigil.exe"

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{6F3C2B1E-8A4D-4C59-9E3B-7D1A2F5C9B40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Jerry Ubah
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases
VersionInfoVersion={#Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} hardware monitor setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Vigil-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\Vigil.App\Assets\vigil.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExe},VigilDiag.exe
RestartApplications=no
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole self-contained publish folder (Vigil.exe, VigilDiag.exe, .NET runtime, libraries).
; portable.txt is excluded: its presence would switch Vigil to portable mode.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Hardware monitor"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Vigil writes this value itself when "Start with Windows" is on. Nothing is created here; the value is
; only removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Vigil"; Flags: uninsdeletevalue

[Run]
; Interactive install: offer to start Vigil.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app update (silent, /UPDATE=1): start the new version automatically.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdateMode

[UninstallRun]
; Remove the optional elevated startup task. Deleting it may need administrator rights; if the user
; declines, uninstall continues and the task can be deleted in Task Scheduler.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Vigil (elevated startup)"" /F"; Flags: runhidden; RunOnceId: "RemoveElevatedTask"; Check: ElevatedTaskExists

[UninstallDelete]
; Downloaded updates and logs. Settings (%APPDATA%\Vigil) are kept so a reinstall keeps your layout.
Type: filesandordirs; Name: "{localappdata}\Vigil\Updates"
Type: filesandordirs; Name: "{localappdata}\Vigil\Logs"
Type: dirifempty; Name: "{localappdata}\Vigil"

[Code]
function IsUpdateMode: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function ElevatedTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "Vigil (elevated startup)"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
end;
