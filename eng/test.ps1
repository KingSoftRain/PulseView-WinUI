param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateRange(1, 64)]
    [int] $Parallel = 2,

    [switch] $SkipManaged,

    [switch] $SkipNative
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

if (-not $SkipManaged) {
    $dotnet = Get-PvDotNetPath
    if (-not $dotnet) {
        throw 'dotnet was not found. Run eng\check-env.ps1 for details.'
    }

    $testProjects = @(Get-PvManagedTestProjects)
    if ($testProjects.Count -eq 0) {
        Write-Warning 'No managed test projects were found under src\*.Tests\.'
    }
    else {
        foreach ($testProject in $testProjects) {
            Write-PvSection "Test managed: $testProject"
            Invoke-PvExternal $dotnet @(
                'test',
                $testProject,
                '--configuration', $Configuration,
                '--no-restore',
                "--logger:trx;LogFileName=$([System.IO.Path]::GetFileNameWithoutExtension($testProject)).trx"
            )
        }
    }
}

if (-not $SkipNative) {
    $nativeBuild = Join-PvPath 'build' 'native'
    $ctest = Resolve-PvToolPath 'ctest'
    if (-not $ctest) {
        Write-Warning 'ctest was not found; native tests were skipped.'
    }
    elseif (-not (Test-Path -LiteralPath $nativeBuild -PathType Container)) {
        Write-Warning 'Native build directory does not exist; native tests were skipped.'
    }
    else {
        Write-PvSection 'Test native'
        Invoke-PvExternal $ctest @(
            '--test-dir', $nativeBuild,
            '-C', $Configuration,
            '--output-on-failure',
            '-j', $Parallel
        )
    }
}
