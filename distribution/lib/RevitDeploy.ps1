#requires -Version 5.1
<#
.SYNOPSIS
    Revit deploy helpers used by the RevitCortex 2026 installer.
    They support machine-scope deployment with user-scope fallback.
#>

function Test-RevitRunning {
    [OutputType([bool])]
    param()
    return [bool](Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
}

function Assert-RevitClosed {
    <#
    .SYNOPSIS
        Verify Revit is not running before plugin DLLs are replaced.
        Interactive mode asks the user to close Revit.
        Non-interactive mode waits for Revit to exit up to TimeoutSeconds.
    #>
    param(
        [switch] $NonInteractive,
        [int] $TimeoutSeconds = 120
    )

    if ($NonInteractive) {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while (Test-RevitRunning) {
            if ((Get-Date) -gt $deadline) {
                throw "Revit is still running after $TimeoutSeconds seconds. Close Revit 2026 and re-run the installer."
            }
            Start-Sleep -Milliseconds 500
        }
        return
    }

    while (Test-RevitRunning) {
        Write-Host ""
        Write-Host "  Revit is currently running. The installer cannot replace plugin DLLs while Revit has them locked." -ForegroundColor Yellow
        $choice = Read-Host "  Close Revit 2026, then press ENTER to continue (or type 'q' to abort)"
        if ($choice -eq 'q' -or $choice -eq 'Q') {
            throw "Installation aborted by user (Revit was running)."
        }
    }
}

function Copy-RevitAddin {
    <#
    .SYNOPSIS
        Copy RevitCortex plugin DLLs and the .addin manifest into the requested
        Revit add-in folder with ACL-aware machine-to-user fallback.

    .DESCRIPTION
        This helper remains version-parameterized for clean path handling, but the
        current fork installer calls it with Version = "2026" only.

        Primary destination:
          C:\ProgramData\Autodesk\Revit\Addins\2026\

        Fallback:
          %APPDATA%\Autodesk\Revit\Addins\2026\

        Revit scans both locations, so after a successful copy the helper removes
        the opposite-scope copy to avoid stale DLL shadowing.

    .PARAMETER Version
        Revit major version. Current fork callers use "2026".

    .PARAMETER PluginSource
        Folder containing the built Revit 2026 plugin DLLs.

    .PARAMETER AddinManifest
        Full path to the RevitCortex.addin XML manifest.
    #>
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $PluginSource,
        [Parameter(Mandatory)] [string] $AddinManifest
    )

    $machineRoot = "C:\ProgramData\Autodesk\Revit\Addins"
    $userRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
    $scopes = @(
        @{ Name = 'machine'; Root = $machineRoot; Other = $userRoot },
        @{ Name = 'user'; Root = $userRoot; Other = $machineRoot }
    )

    $lastError = $null
    foreach ($scope in $scopes) {
        $verDir = Join-Path $scope.Root $Version
        $pluginDir = Join-Path $verDir 'RevitCortex'
        $addinFile = Join-Path $verDir 'RevitCortex.addin'

        try {
            if (-not (Test-Path $verDir)) { New-Item -ItemType Directory -Path $verDir -Force | Out-Null }

            if (Test-Path $pluginDir) { Remove-Item $pluginDir -Recurse -Force -ErrorAction Stop }

            Copy-Item $PluginSource $pluginDir -Recurse -Force -ErrorAction Stop
            Copy-Item $AddinManifest $addinFile -Force -ErrorAction Stop

            Get-ChildItem $pluginDir -Recurse -File | ForEach-Object {
                Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
            }
            Unblock-File -Path $addinFile -ErrorAction SilentlyContinue

            $otherVerDir = Join-Path $scope.Other $Version
            $otherPluginDir = Join-Path $otherVerDir 'RevitCortex'
            $otherAddinFile = Join-Path $otherVerDir 'RevitCortex.addin'
            if (Test-Path $otherPluginDir) {
                try { Remove-Item $otherPluginDir -Recurse -Force -ErrorAction Stop } catch {}
            }
            if (Test-Path $otherAddinFile) {
                try { Remove-Item $otherAddinFile -Force -ErrorAction Stop } catch {}
            }

            return @{ Version = $Version; Scope = $scope.Name; TargetDir = $pluginDir; Ok = $true; Error = $null }
        } catch [System.UnauthorizedAccessException] {
            $lastError = $_
            continue
        } catch {
            $lastError = $_
            continue
        }
    }

    return @{ Version = $Version; Scope = $null; TargetDir = $null; Ok = $false; Error = "$lastError" }
}

function Remove-RevitAddin {
    <#
    .SYNOPSIS
        Remove RevitCortex from both machine and user scope for the requested version.
        Current fork callers use Revit 2026 only.
    #>
    param([Parameter(Mandatory)] [string] $Version)

    $removed = @()
    foreach ($root in @("C:\ProgramData\Autodesk\Revit\Addins", (Join-Path $env:APPDATA 'Autodesk\Revit\Addins'))) {
        $pluginDir = Join-Path $root "$Version\RevitCortex"
        $addinFile = Join-Path $root "$Version\RevitCortex.addin"
        if (Test-Path $pluginDir) {
            try { Remove-Item $pluginDir -Recurse -Force -ErrorAction Stop; $removed += $pluginDir } catch {}
        }
        if (Test-Path $addinFile) {
            try { Remove-Item $addinFile -Force -ErrorAction Stop } catch {}
        }
    }
    return $removed
}
