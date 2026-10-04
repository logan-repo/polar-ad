; PolarAd installer script (Inno Setup 6).
;
; Build: publish the app first, then compile this script.
;   dotnet publish ..\src\PolarAd.App\PolarAd.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\publish
;   iscc PolarAd.iss
;
; What this installer does beyond copying files:
;   - Requires admin (the app itself needs admin for DNS changes / port 53).
;   - Optionally registers autostart as TWO pieces, mirroring AutoStartManager.cs
;     in PolarAd.Core:
;       1. A Scheduled Task (RunLevel=Highest, onlogon trigger) — this is what
;          actually launches PolarAd.exe at logon without a UAC prompt. A
;          plain registry Run key / Startup-folder shortcut pointed straight
;          at PolarAd.exe would re-prompt UAC every login instead.
;       2. A Startup-folder shortcut that runs "schtasks /run /tn ..." — exists
;          purely so PolarAd shows up in Task Manager's Startup apps tab,
;          which only looks at Run keys and the Startup folder and has no
;          concept of Scheduled Tasks. Invoking the pre-elevated task this way
;          still doesn't prompt UAC. PolarAd.App has a single-instance mutex
;          guard because both of these can fire at the same logon.
;   - On uninstall, unconditionally restores DNS to automatic (DHCP) via
;     restore-dns.ps1 and kills any running PolarAd.exe, BEFORE removing
;     files — so an uninstall never leaves the system pointed at a DNS
;     resolver that's about to stop existing.

#define MyAppName "Polar Project"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Polar Project"
#define MyAppExeName "PolarAd.exe"

[Setup]
AppId={{E728B0A6-20EB-408F-8BA5-C2108DF29B05}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=PolarProjectSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\PolarAd.App\Assets\polarad.ico

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "autostart"; Description: "Windows 시작 시 자동 실행 (관리자 권한, 로그온 시)"; GroupDescription: "시작 옵션:"; Flags: checkedonce
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; GroupDescription: "추가 아이콘:"; Flags: unchecked

[InstallDelete]
; v0.1.2/0.1.3 registered autostart via a Startup-folder shortcut; remove it on upgrade
; so it doesn't launch alongside the Run-key entry.
Type: files; Name: "{userstartup}\PolarAd.lnk"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\restore-dns.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
[Registry]
; Run-key entry is what Task Manager's Startup apps tab displays. It invokes the
; pre-elevated scheduled task (no UAC prompt), never PolarAd.exe directly.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "PolarAd"; ValueData: """{sys}\schtasks.exe"" /run /tn ""PolarAd AutoStart"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Register the logon Scheduled Task only if the user kept the "autostart" task checked.
Filename: "{sys}\cmd.exe"; Parameters: "/c schtasks.exe /create /tn ""PolarAd AutoStart"" /tr ""\""{app}\{#MyAppExeName}\"""" /sc onlogon /rl highest /f"; \
    Flags: runhidden; Tasks: autostart; StatusMsg: "자동 실행 작업을 등록하는 중..."



[UninstallRun]
; Order matters: stop the running app FIRST, then unconditionally restore DNS,
; then remove the scheduled task. Every step swallows its own exit code (via
; "|| exit /b 0" / "& exit /b 0") so a step that legitimately has nothing to
; do (app wasn't running, task was never registered) never surfaces an
; uninstaller error dialog.
Filename: "{sys}\cmd.exe"; Parameters: "/c taskkill /IM {#MyAppExeName} /F || exit /b 0"; \
    Flags: runhidden; RunOnceId: "KillPolarAd"
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\restore-dns.ps1"""; \
    Flags: runhidden; RunOnceId: "RestoreDns"
Filename: "{sys}\cmd.exe"; Parameters: "/c schtasks.exe /delete /tn ""PolarAd AutoStart"" /f || exit /b 0"; \
    Flags: runhidden; RunOnceId: "RemoveAutoStartTask"
