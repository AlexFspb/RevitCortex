; RevitCortex 2026 Inno Setup Installer
; This fork targets Autodesk Revit 2026 only.
;
; Build release first:
;   .\build-release.ps1 -Version "1.0.50"
; Then compile this file with Inno Setup 6.

#define MyAppName "RevitCortex 2026"
#define MyAppVersion "1.0.50"
#define MyAppPublisher "AlexFspb"
#define MyAppURL "https://github.com/AlexFspb/RevitCortex"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={userappdata}\.revitcortex
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=RevitCortex-2026-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#MyAppName}
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
italian.WelcomeLabel1=Benvenuto nell'installazione di RevitCortex 2026
italian.WelcomeLabel2=Questo programma installera' RevitCortex per Autodesk Revit 2026.%n%nQuesto fork supporta esclusivamente Revit 2026.%n%nChiudi Revit prima di procedere.
italian.FinishedLabel=L'installazione di RevitCortex 2026 e' completata.%n%nRiavvia Revit 2026 per caricare il plugin.
english.WelcomeLabel1=Welcome to RevitCortex 2026 Setup
english.WelcomeLabel2=This installer deploys RevitCortex for Autodesk Revit 2026 only.%n%nClose Revit before continuing.
english.FinishedLabel=RevitCortex 2026 installation is complete.%n%nRestart Revit 2026 to load the plugin.

[Types]
Name: "full"; Description: "Full installation for Autodesk Revit 2026"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "server"; Description: "MCP Server (required)"; Types: full custom; Flags: fixed
Name: "r26"; Description: "Plugin for Autodesk Revit 2026"; Types: full custom; Check: RevitDirExists('2026')

[InstallDelete]
Type: filesandordirs; Name: "{userappdata}\.revitcortex\server"

[Files]
; MCP server
Source: "..\release\server\*"; DestDir: "{userappdata}\.revitcortex\server"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs

; Revit 2026 plugin
Source: "..\release\plugin\R26\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex"; Components: r26; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; Components: r26; Flags: ignoreversion

; Shared PowerShell helpers
Source: "..\distribution\lib\*"; DestDir: "{userappdata}\.revitcortex\dist-lib"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs

; Install/uninstall scripts for manual re-run
Source: "..\distribution\install.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion
Source: "..\distribution\uninstall.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex.addin"
Type: filesandordirs; Name: "{userappdata}\.revitcortex\server"
Type: filesandordirs; Name: "{userappdata}\.revitcortex\dist-lib"

[Run]
; Best-effort Git install.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\GitInstall.ps1'; Ensure-Git | Out-Null }}"""; \
  StatusMsg: "Checking Git..."; \
  Flags: runhidden waituntilterminated; \
  Components: server

; Configure Claude Desktop without overwriting unrelated MCP servers.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\ClaudeConfig.ps1'; $exe = Join-Path $env:USERPROFILE '.revitcortex\server\RevitCortex.Server.exe'; $cfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; try {{ Merge-ClaudeMcpServer -ConfigPath $cfg -ServerName 'revitcortex' -Command $exe | Out-Null }} catch {{ Write-Host ('Claude Desktop config update failed: ' + $_) }} }}"""; \
  StatusMsg: "Configuring Claude Desktop..."; \
  Flags: runhidden waituntilterminated; \
  Components: server

[UninstallRun]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\ClaudeConfig.ps1'; $cfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; try {{ Remove-ClaudeMcpServer -ConfigPath $cfg -ServerName 'revitcortex' | Out-Null }} catch {{}} }}"""; \
  Flags: runhidden waituntilterminated; \
  RunOnceId: "RemoveClaudeDesktop"

[Code]
function RevitDirExists(Version: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version))
    or DirExists(ExpandConstant('{pf}\Autodesk\Revit ' + Version));
end;
