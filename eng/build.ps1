param(
    [ValidateSet('Debug', 'Release', 'RelWithDebInfo')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string] $Platform = 'x64',

    [ValidateRange(1, 64)]
    [int] $Parallel = 2,

    [switch] $SkipEnvCheck,

    [switch] $SkipNative,

    [switch] $SkipApp,

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

if (-not $SkipEnvCheck) {
    & (Join-Path $PSScriptRoot 'check-env.ps1')
}

if (-not $SkipNative) {
    & (Join-Path $PSScriptRoot 'build-native.ps1') -Configuration $Configuration -Parallel $Parallel
}

if (-not $SkipApp) {
    $appConfiguration = if ($Configuration -eq 'RelWithDebInfo') { 'Release' } else { $Configuration }
    & (Join-Path $PSScriptRoot 'build-app.ps1') -Configuration $appConfiguration -Platform $Platform -Parallel $Parallel -NoRestore:$NoRestore
}
