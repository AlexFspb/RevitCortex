#Requires -Version 5.1
<#
.SYNOPSIS
  Diagnose duplicate RevitCortex 2026 installations across machine and user scope.

.NOTES
  Read-only. This fork supports Autodesk Revit 2026 only.
#>

$ErrorActionPreference = "Stop"
$machineRoot = "C:\ProgramData\Autodesk\Revit\Addins"
$userRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
$ver = "2026"

Write-Host "=== RevitCortex 2026 install diagnostic ===" -ForegroundColor Cyan
Write-Host ""

$machineDll = Join-Path $machineRoot "$ver\RevitCortex\RevitCortex.Plugin.dll"
$userDll = Join-Path $userRoot "$ver\RevitCortex\RevitCortex.Plugin.dll"
$hasMachine = Test-Path $machineDll
$hasUser = Test-Path $userDll

if (-not $hasMachine -and -not $hasUser) {
    Write-Host "No RevitCortex 2026 plugin installation found." -ForegroundColor Yellow
} else {
    Write-Host "Revit 2026:" -ForegroundColor White
    if ($hasMachine) {
        $info = Get-Item $machineDll
        Write-Host ("  machine : {0,7} bytes  {1:yyyy-MM-dd HH:mm}  {2}" -f $info.Length, $info.LastWriteTime, $machineDll) -ForegroundColor Gray
    }
    if ($hasUser) {
        $info = Get-Item $userDll
        Write-Host ("  user    : {0,7} bytes  {1:yyyy-MM-dd HH:mm}  {2}" -f $info.Length, $info.LastWriteTime, $userDll) -ForegroundColor Gray
    }
    if ($hasMachine -and $hasUser) {
        Write-Host "  WARNING: both scopes contain RevitCortex 2026. One copy may shadow the other." -ForegroundColor Red
        Write-Host "  Use deploy.ps1 for machine scope, deploy-userscope.ps1 for user scope, or sync-machine-scope-r26.ps1 to consolidate." -ForegroundColor Yellow
    } else {
        Write-Host "  No duplicate scope detected." -ForegroundColor Green
    }
}

Write-Host ""
$skillPaths = @(
    @{ Name = "Claude Code"; Path = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex\SKILL.md") },
    @{ Name = "Codex CLI"; Path = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex\SKILL.md") }
)
$anyFound = $false
foreach ($entry in $skillPaths) {
    if (Test-Path $entry.Path) {
        Write-Host "  OK skill installed for $($entry.Name): $($entry.Path)" -ForegroundColor Green
        $anyFound = $true
    }
}
if (-not $anyFound) {
    Write-Host "  AI skill not installed in .claude/.codex. Re-run the installer or copy ai-skills/revitcortex manually." -ForegroundColor Yellow
}
