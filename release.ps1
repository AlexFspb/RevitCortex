#requires -Version 5.1
<#
.SYNOPSIS
    Builds a RevitCortex package for Autodesk Revit 2026.

.DESCRIPTION
    Fork-safe release helper. It updates the local plugin/installer version and
    builds the R26 ZIP package. It intentionally does NOT push tags, publish to
    LuDattilo/revitcortex-releases, or modify the upstream update manifest.

    Until this fork gets its own release channel, publishing is a manual step.

.PARAMETER Version
    Semantic version such as 1.0.51.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  OK $msg" -ForegroundColor Green }

Set-Location $RepoRoot

Write-Step "Pre-flight checks"
$gitStatus = & git status --porcelain 2>&1
if ($LASTEXITCODE -ne 0) { throw "Not a git repository or git is not in PATH" }
$dirty = $gitStatus | Where-Object { $_ -and ($_ -notmatch '^\?\?') -and ($_ -notmatch 'settings\.local\.json$') }
if ($dirty) {
    Write-Host "Working tree has uncommitted changes:" -ForegroundColor Red
    $dirty | ForEach-Object { Write-Host "  $_" }
    throw "Commit or stash changes before preparing a release"
}
Write-Ok "working tree clean"

Write-Step "Setting fork version to $Version"

$csproj = Join-Path $RepoRoot 'src\RevitCortex.Plugin\RevitCortex.Plugin.csproj'
$cspText = Get-Content $csproj -Raw
$cspNew = $cspText `
    -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>" `
    -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>" `
    -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
if ($cspNew -eq $cspText) { throw "Could not update version in $csproj" }
[System.IO.File]::WriteAllText($csproj, $cspNew, [System.Text.UTF8Encoding]::new($false))
Write-Ok "Plugin.csproj updated"

$issPath = Join-Path $RepoRoot 'installer\RevitCortex.iss'
$issText = Get-Content $issPath -Raw
$issNew = $issText -replace '#define MyAppVersion "[\d\.]+"', "#define MyAppVersion `"$Version`""
if ($issNew -eq $issText) { throw "Could not update MyAppVersion in $issPath" }
[System.IO.File]::WriteAllText($issPath, $issNew, [System.Text.UTF8Encoding]::new($false))
Write-Ok "RevitCortex.iss updated"

Write-Step "Building Revit 2026 release package"
$buildScript = Join-Path $RepoRoot 'build-release.ps1'
& $buildScript -Version $Version
if ($LASTEXITCODE -ne 0) { throw "build-release.ps1 failed" }

$zipPath = Join-Path $RepoRoot "RevitCortex-v$Version-R26.zip"
if (-not (Test-Path $zipPath)) { throw "Expected ZIP not found: $zipPath" }
$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "================================================" -ForegroundColor Green
Write-Host " RevitCortex 2026 package prepared" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor White
Write-Host "  Target:  Autodesk Revit 2026 only" -ForegroundColor White
Write-Host "  ZIP:     $zipPath ($zipSizeMB MB)" -ForegroundColor White
Write-Host ""
Write-Host "No upstream repository, tag, release or update manifest was modified." -ForegroundColor Yellow
Write-Host "Publish the ZIP manually under AlexFspb/RevitCortex when you are ready." -ForegroundColor Yellow
Write-Host ""
