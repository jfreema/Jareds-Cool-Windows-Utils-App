; The installer, built with Inno Setup 6 by scripts/build.ps1:
;   iscc /DAppVersion=0.1.0 /DSourceDir=<published app folder> /DOutputDir=<folder> installer\JaredsWindowsUtils.iss
; It installs for the current user only, so it needs no administrator rights: into
; %LocalAppData%\Programs\JaredsWindowsUtils, with a Start menu shortcut. Check for Updates runs it silently with
; /RELAUNCH, which opens the new version when it's done.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\out"
#endif
#ifndef OutputName
  #define OutputName "JaredsWindowsUtils-Setup"
#endif

#define AppName "Jared's Windows App"
#define AppFullName "Jared's Cool Windows Utilities App"
#define AppExe "JaredsWindowsUtils.exe"
#define RepoUrl "https://github.com/jfreema/Jareds-Cool-Windows-Utils-App"

[Setup]
; Never change: Windows knows the installed app by it
AppId={{6F1D3C2A-8E4B-4F57-9C1E-5A7B2D9E4C31}
AppName={#AppFullName}
AppVersion={#AppVersion}
AppVerName={#AppFullName} {#AppVersion}
AppPublisher=jfreema
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/blob/main/docs/troubleshooting.md
AppUpdatesURL={#RepoUrl}/releases
DefaultDirName={autopf}\JaredsWindowsUtils
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 version 1809 or later
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\src\JaredsWindowsUtils\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppFullName}
WizardStyle=modern
WizardImageFile=wizard-side-100.bmp,wizard-side-200.bmp
WizardSmallImageFile=wizard-corner-100.bmp,wizard-corner-200.bmp
DisableReadyPage=yes
DisableDirPage=auto
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
; A copy that's running is asked to quit first (PrepareToInstall). This catches one that didn't.
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductName={#AppFullName}
VersionInfoDescription={#AppFullName} Setup
VersionInfoCompany=jfreema

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
FinishedLabel=Setup has installed [name] on your computer. Find it in the taskbar's corner, or under ^ there if Windows tucked it away. Window snapping is on: try Ctrl+Alt+Left on a window.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppFullName}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent
; After Check for Updates
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: Relaunching

[UninstallRun]
; Quits the app and takes it out of Open at Login
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "CleanUp"

[UninstallDelete]
; Left by a portable copy's update, or a crash mid-update
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.new"

[Code]
{ Check for Updates passes /RELAUNCH }
function Relaunching: Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), '/RELAUNCH') = 0 then
      Result := True;
end;

{ The copy that's running quits, so its files can be replaced. The old version's exe does the asking, and waits for it. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
