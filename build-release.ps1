param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ReleaseDir = Join-Path $RepoRoot "release"
$ZipName = "RevitCortex-v$Version-R26.zip"
$ZipPath = Join-Path $RepoRoot $ZipName

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " RevitCortex 2026 Release Builder v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

Write-Host "[1/4] Building Revit 2026 plugin..." -ForegroundColor Yellow

$pluginProject = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.Plugin.csproj"
$toolsProject = Join-Path $RepoRoot "src\RevitCortex.Tools\RevitCortex.Tools.csproj"
$outDir = Join-Path $ReleaseDir "plugin\R26"

Write-Host "  Building Release R26..." -ForegroundColor Gray
dotnet publish -c "Release R26" $pluginProject -o $outDir --no-self-contained -v quiet
if ($LASTEXITCODE -ne 0) { throw "Revit 2026 plugin build failed" }

dotnet publish -c "Release R26" $toolsProject -o $outDir --no-self-contained -v quiet
if ($LASTEXITCODE -ne 0) { throw "Revit 2026 tools build failed" }

$dllCount = (Get-ChildItem "$outDir\*.dll").Count
Write-Host "  R26 built: $dllCount DLLs" -ForegroundColor Green

Write-Host ""
Write-Host "[2/4] Building C# MCP server..." -ForegroundColor Yellow

$serverProject = Join-Path $RepoRoot "src\RevitCortex.Server\RevitCortex.Server.csproj"
$serverTarget = Join-Path $ReleaseDir "server"

dotnet publish $serverProject -c Release -o $serverTarget --self-contained true -r win-x64 -v quiet
if ($LASTEXITCODE -ne 0) { throw "MCP server build failed" }

$exePath = Join-Path $serverTarget "RevitCortex.Server.exe"
if (!(Test-Path $exePath)) { throw "Server executable not found after publish: $exePath" }
Write-Host "  Server built: $exePath" -ForegroundColor Green

Write-Host ""
Write-Host "[3/4] Copying support files..." -ForegroundColor Yellow

Copy-Item (Join-Path $RepoRoot "distribution\install.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\install.bat") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\uninstall.ps1") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\README.txt") $ReleaseDir
Copy-Item (Join-Path $RepoRoot "distribution\LEGGIMI.md") $ReleaseDir

$libTarget = Join-Path $ReleaseDir "lib"
New-Item -ItemType Directory -Path $libTarget -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "distribution\lib\*") $libTarget -Recurse -Force

Copy-Item (Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin") $ReleaseDir

$skillSource = Join-Path $RepoRoot "ai-skills"
$skillTarget = Join-Path $ReleaseDir "ai-skills"
if (Test-Path $skillSource) {
    New-Item -ItemType Directory -Path $skillTarget -Force | Out-Null
    Copy-Item "$skillSource\*" $skillTarget -Recurse -Force
}

$templatesTarget = Join-Path $ReleaseDir "config-templates"
New-Item -ItemType Directory -Path $templatesTarget -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "distribution\config-templates\*") $templatesTarget

Write-Host "  Support files copied." -ForegroundColor Green

Write-Host ""
Write-Host "[4/4] Creating ZIP archive..." -ForegroundColor Yellow

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$ReleaseDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Created: $ZipPath ($sizeMB MB)" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Revit 2026 release package ready" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  File: $ZipName" -ForegroundColor White
Write-Host "  Target: Autodesk Revit 2026 only" -ForegroundColor White
Write-Host ""
