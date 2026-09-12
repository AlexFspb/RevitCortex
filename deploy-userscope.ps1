# Revit 2026 user-scope deploy workaround.
# Installs this fork under %APPDATA% when machine-scope deployment is undesirable.
param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Configuration = "$Config R26"
$PublishDir = Join-Path $RepoRoot "publish\R26-userscope"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"
$UserTargetDir = Join-Path $UserAddinsDir "RevitCortex"

Write-Host "=== RevitCortex 2026 User-Scope Deploy ===" -ForegroundColor Cyan

$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host "ERROR: Revit is running (PID $($revit.Id -join ', ')). Close Revit 2026 first." -ForegroundColor Red
    exit 1
}

$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) {
    $orphans | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

Write-Host "Publishing Plugin (Revit 2026)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }

Write-Host "Publishing Tools (Revit 2026)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

if (-not (Test-Path $UserAddinsDir)) {
    New-Item -ItemType Directory -Path $UserAddinsDir -Force | Out-Null
}
if (Test-Path $UserTargetDir) { Remove-Item $UserTargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $UserTargetDir -Force | Out-Null
Copy-Item "$PublishDir\*" $UserTargetDir -Recurse -Force

$AddinSource = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin"
Copy-Item $AddinSource $UserAddinsDir -Force

$dllCount = (Get-ChildItem "$UserTargetDir\*.dll").Count
Write-Host "OK R26 -> $UserTargetDir ($dllCount DLLs)" -ForegroundColor Green
Write-Host "Restart Autodesk Revit 2026 to load the plugin." -ForegroundColor Green
