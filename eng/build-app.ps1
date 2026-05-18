param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string] $Platform = 'x64',

    [ValidateRange(1, 64)]
    [int] $Parallel = 2,

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

$target = Get-PvSolutionOrProjectPath
if (-not $target) {
    throw 'Managed app project was not found. Expected src\PulseView.App\PulseView.App.csproj or a solution at repository root.'
}

$dotnet = Get-PvDotNetPath
if (-not $dotnet) {
    throw 'dotnet was not found. Run eng\check-env.ps1 for details.'
}

if (-not $NoRestore) {
    Write-PvSection 'Restore app'
    Invoke-PvExternal $dotnet @(
        'restore',
        $target,
        "-p:Platform=$Platform"
    )
}

Write-PvSection 'Build app'
$buildArgs = @(
    'build',
    $target,
    '--configuration', $Configuration,
    "-p:Platform=$Platform",
    "-maxcpucount:$Parallel"
)

if ($NoRestore) {
    $buildArgs += '--no-restore'
}

Invoke-PvExternal $dotnet $buildArgs
