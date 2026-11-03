; Yata installer (Inno Setup 6). Build with tools/build-installer.ps1.
; Per-user install, no admin rights — except installing the .NET runtime when it's missing.

#ifndef AppVersion
  #define AppVersion "0.7.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{8F3C2A51-6D7E-4B9A-9C1D-2E5F7A8B9C0D}
AppName=Yata
AppVersion={#AppVersion}
AppVerName=Yata {#AppVersion}
AppPublisher=ShamilJafarov20
AppPublisherURL=https://github.com/ShamilJafarov20/yata
AppSupportURL=https://github.com/ShamilJafarov20/yata/issues
AppUpdatesURL=https://github.com/ShamilJafarov20/yata/releases
; Code signing (SignPath) checks the product name and version of the installer against the release.
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Yata
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=ShamilJafarov20
VersionInfoDescription=Yata Setup
DefaultDirName={localappdata}\Programs\Yata
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Yata-Setup-{#AppVersion}
SetupIconFile=..\src\Yata\Assets\yata.ico
UninstallDisplayIcon={app}\Yata.exe
UninstallDisplayName=Yata
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Yata in 1-bit pixel art (tools/make-art.ps1). One file per display scale, each exactly the
; size of the image area, so nothing is ever stretched and blurred; any spare room is black.
WizardImageFile=wizard-image-100.png,wizard-image-125.png,wizard-image-150.png,wizard-image-175.png,wizard-image-200.png
WizardSmallImageFile=wizard-small-100.png,wizard-small-125.png,wizard-small-150.png,wizard-small-175.png,wizard-small-200.png
WizardImageStretch=no
WizardImageBackColor=black
WizardSmallImageBackColor=black
; Greet with Yata up front; Inno skips the Welcome page by default, leaving the art for the end.
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Close a running Yata so the exe can be replaced on upgrade.
CloseApplications=force
RestartApplications=no
; The "yata-cli in PATH" task changes the user's PATH: tell open windows about it.
ChangesEnvironment=yes

[Languages]
; What leaves the PC, shown before installing (a SignPath Foundation requirement); full policy in PRIVACY.md.
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "privacy-pt.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "privacy-en.txt"

[CustomMessages]
brazilianportuguese.AutoStart=Iniciar o Yata com o Windows
english.AutoStart=Start Yata with Windows
brazilianportuguese.AddToPath=Pôr o yata-cli no PATH (para scripts e agentes de IA chamarem só "yata-cli")
english.AddToPath=Add yata-cli to PATH (so scripts and AI agents can just call "yata-cli")
brazilianportuguese.RuntimeInstalling=Instalando o .NET 10 Desktop Runtime (Microsoft)...
english.RuntimeInstalling=Installing the .NET 10 Desktop Runtime (Microsoft)...
brazilianportuguese.RuntimeFailed=Não foi possível instalar o .NET 10 Desktop Runtime. Instale-o manualmente em https://dotnet.microsoft.com/download/dotnet/10.0 e rode este instalador de novo.
english.RuntimeFailed=Could not install the .NET 10 Desktop Runtime. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run this setup again.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
; Off unless asked: it changes something outside Yata's own folder.
Name: "addtopath"; Description: "{cm:AddToPath}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Yata.exe"; DestDir: "{app}"; Flags: ignoreversion
; Console side, for scripts and agents (yata-cli activity ...).
Source: "{#PublishDir}\yata-cli.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; The Lobe Icons license, for the agents' marks (Services\BrandMarks.cs).
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Yata"; Filename: "{app}\Yata.exe"
Name: "{autodesktop}\Yata"; Filename: "{app}\Yata.exe"; Tasks: desktopicon

[Registry]
; Same value format the app's own "Start with Windows" switch writes, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Yata"; ValueData: """{app}\Yata.exe"" --autostart"; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Yata"; Flags: deletevalue; Tasks: not autostart
; Remove it on uninstall even if it was turned on later from the app's settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Yata"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Yata.exe"; Description: "{cm:LaunchProgram,Yata}"; Flags: nowait postinstall skipifsilent
; "Update and restart" in Yata runs this setup with /SILENT /UPDATE=1: open Yata again when done.
Filename: "{app}\Yata.exe"; Flags: nowait; Check: IsUpdate

[UninstallRun]
; First, while yata-cli.exe still exists: take Yata's hooks out of Claude Code, Cursor and Codex, so
; none is left pointing at it (Settings → AI agents put them there). Before taskkill, which would stop it too.
Filename: "{app}\Yata.exe"; Parameters: "--remove-agent-hooks"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAgentHooks"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Yata.exe /F"; Flags: runhidden; RunOnceId: "StopYata"

; Note: %LOCALAPPDATA%\Yata (tasks, water log, shelf, lyrics cache) is kept on uninstall on purpose.

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  Root: String;
begin
  Result := False;
  Root := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(Root + '10.*', FindRec) then
  try
    repeat
      if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        Result := FileExists(Root + FindRec.Name + '\PresentationFramework.dll');
    until Result or not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// The runtime installer runs elevated, so it runs only if Windows says Microsoft signed it
// (Authenticode, checked with PowerShell's Get-AuthenticodeSignature). The download comes from a
// floating aka.ms link, so a pinned hash would go stale with every .NET patch.
function IsMicrosoftSigned(FileName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "$s = Get-AuthenticodeSignature -LiteralPath ''' + FileName + '''; ' +
    'if ($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -match ''O=Microsoft Corporation'') { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Runs for both interactive and silent installs, right before files are copied.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Installer: String;
begin
  Result := '';
  if IsDesktopRuntimeInstalled then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  try
    // Straight from Microsoft over HTTPS, and checked to be Microsoft-signed before it runs.
    DownloadTemporaryFile('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '', nil);
    Installer := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
    if not IsMicrosoftSigned(Installer) then
    begin
      Result := CustomMessage('RuntimeFailed');
      Exit;
    end;
    // ShellExec (not Exec) so Windows can show the UAC prompt the runtime installer needs.
    if not ShellExec('', Installer, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or
       ((ResultCode <> 0) and (ResultCode <> 3010)) or not IsDesktopRuntimeInstalled then
      Result := CustomMessage('RuntimeFailed');
  except
    Result := CustomMessage('RuntimeFailed') + #13#10#13#10 + GetExceptionMessage;
  end;
end;

// The user's PATH without Yata's folder, however it was spelled there.
function PathWithout(Path, Dir: String): String;
var
  Rest, Part: String;
  P: Integer;
begin
  Result := '';
  Rest := Path;
  while Rest <> '' do
  begin
    P := Pos(';', Rest);
    if P = 0 then
    begin
      Part := Rest;
      Rest := '';
    end
    else
    begin
      Part := Copy(Rest, 1, P - 1);
      Delete(Rest, 1, P);
    end;
    if (Part <> '') and (CompareText(RemoveBackslashUnlessRoot(Part), RemoveBackslashUnlessRoot(Dir)) <> 0) then
    begin
      if Result <> '' then
        Result := Result + ';';
      Result := Result + Part;
    end;
  end;
end;

// Adds or removes Yata's folder in the user's own PATH (HKCU, never the machine's). Written back as
// an expandable string, so entries like %USERPROFILE%in stay as they were.
procedure SetUserPath(Add: Boolean);
var
  Path, NewPath: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
    Path := '';
  NewPath := PathWithout(Path, ExpandConstant('{app}'));
  if Add then
  begin
    if NewPath <> '' then
      NewPath := NewPath + ';';
    NewPath := NewPath + ExpandConstant('{app}');
  end;
  if NewPath <> Path then
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', NewPath);
end;

// Unticking the task on a later install takes the folder back out.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SetUserPath(WizardIsTaskSelected('addtopath'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    SetUserPath(False);
end;
