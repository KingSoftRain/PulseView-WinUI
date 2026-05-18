[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch] $StopProcess
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

$sourceManifest = Join-PvPath 'src' 'PulseView.App' 'Package.appxmanifest'
$manifestInfo = Get-PvAppxManifestInfo -ManifestPath $sourceManifest
$packages = @(Get-PvInstalledAppPackage -IdentityName $manifestInfo.IdentityName)

if ($packages.Count -eq 0) {
    Write-Host "Development package is not installed: $($manifestInfo.IdentityName)"
    return
}

if ($StopProcess) {
    Get-Process PulseView.App -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-PvSection 'Uninstall development package'
foreach ($package in $packages) {
    if ($PSCmdlet.ShouldProcess($package.PackageFullName, 'Remove-AppxPackage')) {
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "Removed $($package.PackageFullName)"
    }
}
