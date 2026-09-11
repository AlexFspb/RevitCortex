param(
    [ValidateSet("2026")]
    [string]$RevitVersion = "2026",
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug"
)

# RevitCortex fork deployment: Autodesk Revit 2026 only.
$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Configuration = "$Config R26"
$PublishDir = Join-Path $RepoRoot "publish\R26"
$AddInsDir = "C:\ProgramData\Autodesk\Revit\Addins\2026"
$TargetDir = Join-Path $AddInsDir "RevitCortex"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2026"
$UserTargetDir = Join-Path $UserAddinsDir "RevitCortex"

Write-Host "=== RevitCortex Deploy ===" -ForegroundColor Cyan
Write-Host "Revit: 2026 | Config: $Configuration"
Write-Host "Target: $TargetDir"

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

Write-Host "`nPublishing Plugin for Revit 2026..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }

Write-Host "Publishing Tools for Revit 2026..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

if (Test-Path $UserTargetDir) {
    Write-Host "Removing competing user-scope install: $UserTargetDir" -ForegroundColor Yellow
    Remove-Item $UserTargetDir -Recurse -Force
}
$userAddinManifest = Join-Path $UserAddinsDir "RevitCortex.addin"
if (Test-Path $userAddinManifest) { Remove-Item $userAddinManifest -Force }

if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force

$AddinSource = Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin"
Copy-Item $AddinSource $AddInsDir -Force

$dllCount = (Get-ChildItem "$TargetDir\*.dll").Count

$skillSrc = Join-Path $RepoRoot "ai-skills\revitcortex"
if (Test-Path $skillSrc) {
    $skillTargets = @(
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".claude"); Target = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex"); Name = "Claude Code" },
        @{ ClientRoot = (Join-Path $env:USERPROFILE ".codex"); Target = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex"); Name = "Codex CLI" }
    )
    foreach ($entry in $skillTargets) {
        if (Test-Path $entry.ClientRoot) {
            if (-not (Test-Path $entry.Target)) { New-Item -ItemType Directory -Path $entry.Target -Force | Out-Null }
            Copy-Item "$skillSrc\*" $entry.Target -Recurse -Force
            Write-Host "Skill synced -> $($entry.Target)" -ForegroundColor Green
        }
    }
}

Write-Host "`n=== Deploy complete ===" -ForegroundColor Green
Write-Host "$dllCount DLLs deployed to $TargetDir"
Write-Host ".addin manifest copied to $AddInsDir\RevitCortex.addin"
Write-Host "`nRestart Revit 2026 to load the plugin."
