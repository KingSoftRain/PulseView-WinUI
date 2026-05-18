param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string] $Platform = 'x64',

    [string] $PackageDir = '',

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

$appProject = Get-PvAppProjectPath
if (-not $appProject) {
    throw 'App project was not found. Expected src\PulseView.App\PulseView.App.csproj.'
}

if (-not $PackageDir) {
    $PackageDir = Join-PvPath 'artifacts' 'packages'
}

$PackageDir = Assert-PvPathInsideRoot $PackageDir
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-app.ps1') -Configuration $Configuration -Platform $Platform
}

$msbuild = Get-PvMSBuildPath
if (-not $msbuild) {
    throw 'MSBuild was not found. Run eng\check-env.ps1 for details.'
}

Write-PvSection 'Pack MSIX'
Invoke-PvExternal $msbuild @(
    $appProject,
    '/t:Publish',
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    '/p:AppxBundle=Always',
    '/p:UapAppxPackageBuildMode=SideloadOnly',
    "/p:AppxPackageDir=$PackageDir\"
)
