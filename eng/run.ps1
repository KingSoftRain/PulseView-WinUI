param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string] $Platform = 'x64',

    [switch] $NoBuild,

    [switch] $Reregister,

    [switch] $NoLaunch,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $AppArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

$appProject = Get-PvAppProjectPath
if (-not $appProject) {
    throw 'App project was not found. Expected src\PulseView.App\PulseView.App.csproj.'
}

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-app.ps1') -Configuration $Configuration -Platform $Platform
}

if ($AppArgs.Count -gt 0) {
    Write-Warning 'App arguments are currently ignored when launching the packaged app through shell:AppsFolder.'
}

$outputDirectory = Get-PvAppBuildOutputDirectory -Configuration $Configuration -Platform $Platform
$manifestPath = Join-Path $outputDirectory 'AppxManifest.xml'
$manifestInfo = Get-PvAppxManifestInfo -ManifestPath $manifestPath

Write-PvSection 'Register development package'
$packages = @(Get-PvInstalledAppPackage -IdentityName $manifestInfo.IdentityName)
if ($Reregister -and $packages.Count -gt 0) {
    foreach ($package in $packages) {
        Write-Host "Removing $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName
    }

    $packages = @()
}

$package = $null
if ($packages.Count -gt 0) {
    $package = $packages[0]
    $installedLocation = [System.IO.Path]::GetFullPath($package.InstallLocation)
    $expectedLocation = [System.IO.Path]::GetFullPath($outputDirectory)
    if (-not $installedLocation.Equals($expectedLocation, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "A different development package is already registered at $installedLocation. Re-run with -Reregister to replace it."
    }

    Write-Host "Already registered: $($package.PackageFullName)"
}
else {
    Add-AppxPackage -Register $manifestPath -ForceApplicationShutdown
    $package = @(Get-PvInstalledAppPackage -IdentityName $manifestInfo.IdentityName) | Select-Object -First 1
    if (-not $package) {
        throw "Package registration completed, but the package could not be found: $($manifestInfo.IdentityName)"
    }

    Write-Host "Registered: $($package.PackageFullName)"
}

if ($NoLaunch) {
    return
}

Write-PvSection 'Launch app'
$aumid = "$($package.PackageFamilyName)!$($manifestInfo.ApplicationId)"
Write-Host "Launching $aumid"
Start-Process "shell:AppsFolder\$aumid"
