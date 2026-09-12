#requires -Version 5.0
<#
.SYNOPSIS
  Synchronizes or removes the machine-scope RevitCortex installation for Autodesk Revit 2026.

.DESCRIPTION
  RevitCortex 2026 can exist in both user and machine scope. Revit scans both locations,
  so duplicate installs can cause an older DLL to shadow the newer one.

  Default action: copy the current Release R26 Plugin/Core/Tools DLLs into the existing
  machine-scope installation.

  With -RemoveMachineScopeOnly: delete the Revit 2026 machine-scope folder and manifest,
  leaving user scope as the only active installation.
#>

param(
    [string]$RepoRoot,
    [switch]$RemoveMachineScopeOnly
)

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".")).Path
}

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Re-launching with admin elevation..." -ForegroundColor Yellow
    $argsList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-RepoRoot", "`"$RepoRoot`""
    )
    if ($RemoveMachineScopeOnly) { $argsList += "-RemoveMachineScopeOnly" }
    Start-Process powershell -ArgumentList $argsList -Verb RunAs -Wait
    exit
}

$ErrorActionPreference = "Stop"

$SrcPlugin = Join-Path $RepoRoot "src\RevitCortex.Plugin\bin\Release R26\net8.0-windows10.0.19041.0"
$SrcTools = Join-Path $RepoRoot "src\RevitCortex.Tools\bin\Release R26\net8.0-windows10.0.19041.0"
$DstDir = "C:\ProgramData\Autodesk\Revit\Addins\2026\RevitCortex"
$DstManifest = "C:\ProgramData\Autodesk\Revit\Addins\2026\RevitCortex.addin"

Write-Host "=== RevitCortex 2026 machine-scope sync ===" -ForegroundColor Cyan

$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close Revit 2026 first." -ForegroundColor Red
    exit 1
}

if ($RemoveMachineScopeOnly) {
    Write-Host "Mode: REMOVE Revit 2026 machine-scope installation." -ForegroundColor Yellow
    if (Test-Path $DstManifest) { Remove-Item $DstManifest -Force }
    if (Test-Path $DstDir) { Remove-Item $DstDir -Recurse -Force }
    Write-Host "DONE. User-scope can now be the only active RevitCortex 2026 install." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $DstDir)) {
    Write-Host "Machine-scope RevitCortex 2026 folder does not exist. Nothing to sync." -ForegroundColor Yellow
    exit 0
}

$files = @(
    "$SrcPlugin\RevitCortex.Plugin.dll",
    "$SrcPlugin\RevitCortex.Plugin.pdb",
    "$SrcPlugin\RevitCortex.Core.dll",
    "$SrcPlugin\RevitCortex.Core.pdb",
    "$SrcTools\RevitCortex.Tools.dll",
    "$SrcTools\RevitCortex.Tools.pdb"
)

foreach ($f in $files) {
    if (Test-Path $f) {
        Copy-Item $f $DstDir -Force
        Write-Host "  Updated: $(Split-Path $f -Leaf)" -ForegroundColor Green
    } else {
        Write-Host "  Missing build output: $f" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "DONE. Restart Autodesk Revit 2026 to load the updated DLLs." -ForegroundColor Green
