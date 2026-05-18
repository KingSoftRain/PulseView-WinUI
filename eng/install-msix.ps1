[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $PackagePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

if (-not $PackagePath) {
    $packageRoot = Join-PvPath 'artifacts' 'packages'
    $package = Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Include '*.msix', '*.msixbundle', '*.appx', '*.appxbundle' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $package) {
        throw 'No MSIX/AppX package was found under artifacts\packages. Run eng\pack-msix.ps1 first or pass -PackagePath.'
    }

    $PackagePath = $package.FullName
}

$PackagePath = Assert-PvPathInsideRoot $PackagePath
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Package was not found: $PackagePath"
}

Write-PvSection 'Install MSIX'
if ($PSCmdlet.ShouldProcess($PackagePath, 'Add-AppxPackage')) {
    Add-AppxPackage -Path $PackagePath
}
