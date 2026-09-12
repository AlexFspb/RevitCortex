#requires -Version 5.1
param(
    [switch] $Silent
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $ScriptDir 'lib\ClaudeConfig.ps1')
. (Join-Path $ScriptDir 'lib\RevitDeploy.ps1')
. (Join-Path $ScriptDir 'lib\GitInstall.ps1')

# This fork supports Autodesk Revit 2026 only.
$RevitVersion = "2026"
$PluginSuffix = "R26"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    $silentArg = if ($Silent) { ' -Silent' } else { '' }
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`"$silentArg"
    exit
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " RevitCortex 2026 Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[0/5] Pre-flight checks..." -ForegroundColor Yellow
try {
    Assert-RevitClosed -NonInteractive:$Silent
    Write-Host "  Revit is not running" -ForegroundColor Gray
} catch {
    Write-Host "  $_" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

Write-Host ""
Write-Host "[1/5] Detecting Autodesk Revit 2026..." -ForegroundColor Yellow

$machineAddinsRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$machineVerDir = Join-Path $machineAddinsRoot $RevitVersion
$revitInstalled = Test-Path $machineVerDir

if (-not $revitInstalled) {
    foreach ($rp in @(
        "HKLM:\SOFTWARE\Autodesk\Revit\2026",
        "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\2026",
        "HKCU:\SOFTWARE\Autodesk\Revit\2026"
    )) {
        if (Test-Path $rp) { $revitInstalled = $true; break }
    }
}

if (-not $revitInstalled) {
    $revitExe = "C:\Program Files\Autodesk\Revit 2026\Revit.exe"
    if (Test-Path $revitExe) { $revitInstalled = $true }
}

if (-not $revitInstalled) {
    Write-Host "  ERROR: Autodesk Revit 2026 was not found." -ForegroundColor Red
    Write-Host "  This fork does not install plugins for Revit 2023, 2024, 2025 or 2027." -ForegroundColor Yellow
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

$pluginDir = Join-Path $ScriptDir "plugin\$PluginSuffix"
if (-not (Test-Path $pluginDir)) {
    Write-Host "  ERROR: This package does not contain plugin\R26." -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

Write-Host "  Autodesk Revit 2026 detected" -ForegroundColor Green

try {
    if (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue) {
        Write-Host "  WARNING: Port 8080 is already in use. Change the RevitCortex port in Settings if needed." -ForegroundColor Yellow
    }
} catch {}

Write-Host ""
Write-Host "[2/5] Installing Revit 2026 plugin..." -ForegroundColor Yellow

$addinTemplate = Join-Path $ScriptDir "RevitCortex.addin"
if (-not (Test-Path $addinTemplate)) {
    Write-Host "  ERROR: RevitCortex.addin not found at $addinTemplate" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

$r = Copy-RevitAddin -Version $RevitVersion -PluginSource $pluginDir -AddinManifest $addinTemplate
if (-not $r.Ok) {
    Write-Host "  Revit 2026 plugin install FAILED: $($r.Error)" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

$tag = if ($r.Scope -eq 'user') { ' (user scope)' } else { '' }
Write-Host "  Installed: $($r.TargetDir)$tag" -ForegroundColor Green

Write-Host ""
Write-Host "[3/5] Installing MCP server..." -ForegroundColor Yellow

$serverSource = Join-Path $ScriptDir "server"
$serverTarget = Join-Path $env:USERPROFILE ".revitcortex\server"

if (-not (Test-Path $serverSource)) {
    Write-Host "  ERROR: Server files not found at $serverSource" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

if (Test-Path $serverTarget) { Remove-Item $serverTarget -Recurse -Force }
New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
Copy-Item "$serverSource\*" $serverTarget -Recurse -Force

$serverExe = Join-Path $serverTarget "RevitCortex.Server.exe"
if (-not (Test-Path $serverExe)) {
    Write-Host "  ERROR: RevitCortex.Server.exe was not found after copy." -ForegroundColor Red
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

Get-ChildItem $serverTarget -Recurse -File | ForEach-Object {
    Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
}

if (-not $Silent -and (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
    $existing = @()
    try {
        $mp = Get-MpPreference -ErrorAction SilentlyContinue
        if ($mp -and $mp.ExclusionPath) { $existing = @($mp.ExclusionPath) }
    } catch {}

    if ($existing -notcontains $serverTarget) {
        $a = Read-Host "  Add Windows Defender exclusion for '$serverTarget'? (y/N)"
        if ($a -eq 'y' -or $a -eq 'Y') {
            Add-MpPreference -ExclusionPath $serverTarget -ErrorAction SilentlyContinue
            Write-Host "  Defender exclusion added" -ForegroundColor Gray
        }
    }
}

Write-Host "  Server installed: $serverExe" -ForegroundColor Green

$skillSrc = Join-Path $ScriptDir "ai-skills\revitcortex"
if (Test-Path $skillSrc) {
    $skillTargets = @(
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".claude"); Target = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex"); Name = "Claude Code" },
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".codex"); Target = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex"); Name = "Codex CLI" }
    )
    foreach ($entry in $skillTargets) {
        if (Test-Path $entry.ClientRoot) {
            New-Item -ItemType Directory -Path $entry.Target -Force | Out-Null
            Copy-Item "$skillSrc\*" $entry.Target -Recurse -Force
            Write-Host "  Installed skill -> $($entry.Target)"
        }
    }
}

Write-Host ""
Write-Host "[4/5] Checking Git..." -ForegroundColor Yellow
$gitOk = Ensure-Git

Write-Host ""
Write-Host "[5/5] Configure Claude client" -ForegroundColor Yellow

if ($Silent) {
    $choice = "1"
} else {
    Write-Host "  [1] Claude Desktop"
    Write-Host "  [2] Claude Code (CLI)"
    Write-Host "  [3] Both"
    Write-Host "  [4] Skip"
    $choice = Read-Host "  Enter choice (1-4)"
}

$claudeDesktopConfigured = $false
$claudeCodeConfigured = $false

if ($choice -eq "1" -or $choice -eq "3") {
    $configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
    try {
        Merge-ClaudeMcpServer -ConfigPath $configPath -ServerName 'revitcortex' -Command $serverExe -Arguments @() | Out-Null
        $claudeDesktopConfigured = $true
        Write-Host "  Claude Desktop configured." -ForegroundColor Green
    } catch {
        Write-Host "  Claude Desktop config update failed: $_" -ForegroundColor Yellow
    }
}

if ($choice -eq "2" -or $choice -eq "3") {
    $claudeCli = Get-Command claude -ErrorAction SilentlyContinue
    if ($claudeCli) {
        try {
            & claude mcp add revitcortex $serverExe 2>$null | Out-Null
            $claudeCodeConfigured = $true
            Write-Host "  Claude Code configured." -ForegroundColor Green
        } catch {
            Write-Host "  Claude Code configuration failed: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Claude Code CLI not found. Configure manually:" -ForegroundColor Yellow
        Write-Host "    claude mcp add revitcortex `"$serverExe`"" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " RevitCortex 2026 installed successfully" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Plugin: Autodesk Revit 2026" -ForegroundColor White
Write-Host "  Server: $serverExe" -ForegroundColor White
Write-Host ("  Git:    {0}" -f $(if ($gitOk) { 'OK' } else { 'NOT installed' })) -ForegroundColor White
if ($claudeDesktopConfigured) { Write-Host "  Client: Claude Desktop" -ForegroundColor White }
if ($claudeCodeConfigured) { Write-Host "  Client: Claude Code" -ForegroundColor White }
Write-Host ""
Write-Host "  Restart Revit 2026 and your MCP client." -ForegroundColor Yellow
Write-Host ""
if (-not $Silent) { Read-Host "Press Enter to close" }
