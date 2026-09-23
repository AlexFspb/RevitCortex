# Launch a separate Revit 2026 process with its own Cortex port.
# Compatible with Windows PowerShell 5.1 and PowerShell 7.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int]$Port,

    [string]$RevitPath = 'C:\Program Files\Autodesk\Revit 2026\Revit.exe'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $RevitPath -PathType Leaf)) {
    throw "Revit executable not found: $RevitPath. Supply -RevitPath for a custom installation."
}

$listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if ($listeners | Where-Object { $_.Port -eq $Port }) {
    throw "TCP port $Port is already in use. Choose a different port for this Revit instance."
}

if ($PSCmdlet.ShouldProcess($RevitPath, "Start Revit with Cortex port $Port")) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = (Get-Item -LiteralPath $RevitPath).FullName
    $startInfo.WorkingDirectory = Split-Path -Parent $startInfo.FileName
    $startInfo.UseShellExecute = $false
    # Set only the child's environment: no setx, settings.json edits or parent changes.
    $startInfo.EnvironmentVariables['REVITCORTEX_PORT'] = $Port.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $revitProcess = [System.Diagnostics.Process]::Start($startInfo)
    Write-Host "Started Revit PID $($revitProcess.Id) with Cortex port $Port. Open a project and enable Cortex Switch."
    $revitProcess.Dispose()
}
