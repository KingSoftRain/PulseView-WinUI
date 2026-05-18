param(
    [ValidateSet('Debug', 'Release', 'RelWithDebInfo')]
    [string] $Configuration = 'Debug',

    [ValidateRange(1, 64)]
    [int] $Parallel = 2,

    [ValidateSet('Ninja')]
    [string] $Generator = 'Ninja',

    [switch] $Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

$sourceRoot = Get-PvNativeSourceRoot
if (-not $sourceRoot) {
    throw 'Native CMake project was not found. Expected CMakeLists.txt at repository root, src\, or src\PulseView.Core.Native\.'
}

$cmake = Get-PvCMakePath
if (-not $cmake) {
    throw 'cmake was not found. Run eng\check-env.ps1 for details.'
}

$ninja = Get-PvNinjaPath
if (-not $ninja) {
    throw 'ninja was not found. Run eng\check-env.ps1 for details.'
}

$env:PATH = ("{0};{1}" -f (Split-Path -Parent $ninja), $env:PATH)
Import-PvVsDevEnvironment -Architecture x64

$buildDir = Join-PvPath 'build' 'native'
if ($Clean -and (Test-Path -LiteralPath $buildDir)) {
    Remove-Item -LiteralPath $buildDir -Recurse -Force
}

New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

Write-PvSection 'Configure native'
Invoke-PvExternal $cmake @(
    '-S', $sourceRoot,
    '-B', $buildDir,
    '-G', $Generator,
    "-DCMAKE_BUILD_TYPE=$Configuration"
)

Write-PvSection 'Build native'
Invoke-PvExternal $cmake @(
    '--build', $buildDir,
    '--config', $Configuration,
    '--parallel', $Parallel
)
