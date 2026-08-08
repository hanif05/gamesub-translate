; Inno Setup script for GameSubTranslate.
; Builds framework-dependent installer; user must already have .NET 8 Desktop Runtime
; (prereq is checked in [Code] below). Output: installer/Output/GameSubTranslate-Setup-{version}.exe.

#define MyAppName "GameSubTranslate"
#define MyAppPublisher "hanif05"
#define MyAppURL "https://github.com/hanif05/game-sub-translate"
#define MyAppExeName "GameSubTranslate.App.exe"
#define MyAppCopyright "Personal use only"

; Read version from src/GameSubTranslate.App/version.txt (T44 writes this file during publish).
#define MyAppVersion GetFileContent("..\src\GameSubTranslate.App\version.txt")

[Setup]
AppId={{B9C5E1A2-3F4D-4E6A-8C7B-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppCopyright={##MyAppCopyright}
AppComments=Auto-translate subtitle game via screen capture + OCR + AI.
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
InfoBeforeFile=
OutputDir=Output
OutputBaseFilename=GameSubTranslate-Setup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} setup
VersionInfoProductName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Framework-dependent publish output. T44 puts the folder under installer/publish-output/.
; Inno strips the source path — every file ends up flat under {app} (or under subfolders we
; recreate via Source: ... DestDir: ...).
Source: "publish-output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean per-user state on uninstall so a reinstall starts truly fresh. The data dir is
; %APPDATA%\GameSubTranslate — leftover settings/logs from a previous install would otherwise
; mask setup bugs. Keep type: filesandordirs in case logs were rotated.
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"

[Code]
// T43: hard-prereq check. Framework-dependent publish = user MUST have .NET 8 Desktop
// Runtime or the app fails on first launch with a generic error. Check the shared-fx
// registry key the .NET installer writes; bail out with a download link if absent.
function IsDotNet8DesktopRuntimeInstalled(): Boolean;
var
  Key: string;
begin
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Result := RegKeyExists(HKLM, Key);
  if not Result then
  begin
    // .NET installer under WOW6432Node is the typical 32-bit-on-64-bit layout; check that too.
    Result := RegKeyExists(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
  Choice: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled() then
  begin
    // Two buttons: Download (launches official runtime download) or Cancel (abort setup).
    Choice := MsgBox(
      'GameSubTranslate requires the .NET 8 Desktop Runtime.' + #13#10 + #13#10 +
      'It was not found on this computer. Click Download to open the official ' +
      'Microsoft download page, then run this installer again.' + #13#10 + #13#10 +
      'Click Cancel to abort this installation.',
      mbConfirmation, MB_YESNO
    );
    if Choice = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ErrorCode);
    end;
    Result := False;  // abort install in both cases — the user must install the runtime first
  end;
end;
