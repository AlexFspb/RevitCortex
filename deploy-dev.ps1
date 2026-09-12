param(
    [ValidateSet("2026")]
    [string]$RevitVersion = "2026",
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug"
)

# --- Revit 2026 dev-only side-by-side deploy ---
# This fork intentionally targets Autodesk Revit 2026 only.
# The dev build installs into a separate user-scope folder/manifest/assembly identity/port
# so it can run next to a production RevitCortex installation without touching prod files.

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Configuration = "$Config R26"
$PublishDir = Join-Path $RepoRoot "publish\R26-dev"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"
$TargetDir = Join-Path $UserAddinsDir "RevitCortexDev"

$DevAddInId = "d3f8a2c4-9b1e-4e5f-8a7c-2f6d0b9e4a11"

Write-Host "=== RevitCortex DEV Deploy ===" -ForegroundColor Cyan
Write-Host "Revit: 2026 | Config: $Configuration"
Write-Host "Target: $TargetDir (user-scope only)"

if ($TargetDir -notlike "*RevitCortexDev") {
    throw "Refusing to deploy: TargetDir '$TargetDir' does not end with 'RevitCortexDev'."
}
$leafName = Split-Path $TargetDir -Leaf
if ($leafName -eq "RevitCortex") {
    throw "Refusing to deploy into the production RevitCortex folder."
}

$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host ""
    Write-Host "ERROR: Revit is currently running (PID $($revit.Id -join ', ')). Close Revit and re-run." -ForegroundColor Red
    exit 1
}

$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) {
    Write-Host "Killing $($orphans.Count) orphan RevitCortex.Server process(es)..." -ForegroundColor Yellow
    $orphans | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

Write-Host "`nPublishing Plugin (Revit 2026 dev)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained -p:DevBuild=true
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }

Write-Host "Publishing Tools (Revit 2026 dev)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

if ($leafName -eq "RevitCortex" -or $TargetDir -notlike "*RevitCortexDev") {
    throw "Refusing to write: TargetDir '$TargetDir' failed the production-folder guard."
}

if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force

if (-not (Test-Path $UserAddinsDir)) {
    New-Item -ItemType Directory -Path $UserAddinsDir -Force | Out-Null
}
$DevManifestPath = Join-Path $UserAddinsDir "RevitCortexDev.addin"
$manifestXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitCortex Dev</Name>
    <Assembly>RevitCortexDev\RevitCortex.Plugin.Dev.dll</Assembly>
    <FullClassName>RevitCortex.Plugin.RevitCortexApp</FullClassName>
    <AddInId>$DevAddInId</AddInId>
    <VendorId>RevitCortex</VendorId>
    <VendorDescription>RevitCortex MCP Server for Autodesk Revit 2026 (Dev build)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
Set-Content -Path $DevManifestPath -Value $manifestXml -Encoding UTF8

$dllCount = (Get-ChildItem "$TargetDir\*.dll").Count
Write-Host "`n=== Dev deploy complete ===" -ForegroundColor Green
Write-Host "$dllCount DLLs deployed to $TargetDir"
Write-Host ".addin manifest written to $DevManifestPath"
Write-Host "`nRestart Revit 2026 to load the dev plugin."
