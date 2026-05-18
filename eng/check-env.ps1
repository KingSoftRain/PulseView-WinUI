param(
    [switch] $Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

Initialize-PvDirectories

$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [ValidateSet('Pass', 'Warn', 'Fail')]
        [string] $Status,

        [string] $Detail = ''
    )

    $script:checks.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Detail = $Detail
    }) | Out-Null
}

function Get-VersionLine {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [string[]] $Arguments = @('--version')
    )

    try {
        $output = & $FilePath @Arguments 2>&1
        return (($output | Select-Object -First 1) -as [string])
    }
    catch {
        return $_.Exception.Message
    }
}

try {
    $os = Get-CimInstance Win32_OperatingSystem
    $osVersion = [version]$os.Version
    if ($osVersion.Build -ge 22000) {
        Add-Check 'Windows 11' 'Pass' ("{0} build {1}" -f $os.Caption, $os.Version)
    }
    else {
        Add-Check 'Windows 11' 'Fail' ("Detected {0} build {1}" -f $os.Caption, $os.Version)
    }
}
catch {
    $fallbackVersion = [System.Environment]::OSVersion.Version
    if ($fallbackVersion.Build -ge 22000) {
        Add-Check 'Windows 11' 'Pass' ("build {0} (Get-CimInstance unavailable: {1})" -f $fallbackVersion, $_.Exception.Message)
    }
    else {
        Add-Check 'Windows 11' 'Warn' ("Unable to confirm Windows 11: {0}" -f $_.Exception.Message)
    }
}

$pwsh = Get-PvPwshPath
if ($pwsh) {
    Add-Check 'PowerShell 7 pwsh' 'Pass' ("{0} ({1})" -f (Get-VersionLine $pwsh @('--version')), $pwsh)
}
else {
    Add-Check 'PowerShell 7 pwsh' 'Fail' 'pwsh was not found in PATH or the standard PowerShell 7 install path.'
}

$git = Get-PvGitPath
if ($git) {
    Add-Check 'Git for Windows' 'Pass' ("{0} ({1})" -f (Get-VersionLine $git @('--version')), $git)
}
else {
    Add-Check 'Git for Windows' 'Fail' 'git was not found.'
}

$dotnet = Get-PvDotNetPath
if ($dotnet) {
    $sdkLines = @(& $dotnet --list-sdks 2>&1)
    $hasDotNet10 = @($sdkLines | Where-Object { $_ -match '^10\.' }).Count -gt 0
    if ($hasDotNet10) {
        Add-Check '.NET 10 SDK' 'Pass' (($sdkLines | Where-Object { $_ -match '^10\.' }) -join '; ')
    }
    else {
        Add-Check '.NET 10 SDK' 'Fail' ('Installed SDKs: ' + ($sdkLines -join '; '))
    }
}
else {
    Add-Check '.NET 10 SDK' 'Fail' 'dotnet was not found.'
}

$cmake = Get-PvCMakePath
if ($cmake) {
    Add-Check 'CMake' 'Pass' ("{0} ({1})" -f (Get-VersionLine $cmake @('--version')), $cmake)
}
else {
    Add-Check 'CMake' 'Fail' 'cmake was not found.'
}

$vsPath = Get-PvVsInstallationPath
if ($vsPath) {
    Add-Check 'Visual Studio Build Tools' 'Pass' $vsPath
}
else {
    Add-Check 'Visual Studio Build Tools' 'Fail' 'Build Tools with MSVC x64/x86 tools were not found by vswhere.'
}

$msbuild = Get-PvMSBuildPath
if ($msbuild) {
    Add-Check 'MSBuild' 'Pass' $msbuild
}
else {
    Add-Check 'MSBuild' 'Fail' 'MSBuild was not found in PATH or Build Tools.'
}

$ninja = Get-PvNinjaPath
if ($ninja) {
    Add-Check 'Ninja' 'Pass' $ninja
}
else {
    Add-Check 'Ninja' 'Fail' 'ninja was not found in PATH or Build Tools.'
}

$sdkVersions = Get-PvWindowsSdkVersions
if ($sdkVersions.Count -gt 0) {
    Add-Check 'Windows 11 SDK' 'Pass' ($sdkVersions -join '; ')
}
else {
    Add-Check 'Windows 11 SDK' 'Fail' 'No Windows 10/11 SDK Lib versions were found.'
}

$vcpkg = Get-PvVcpkgPath
if ($vcpkg) {
    Add-Check 'vcpkg' 'Pass' $vcpkg
}
else {
    Add-Check 'vcpkg' 'Fail' 'vcpkg was not found in PATH, C:\vcpkg, %USERPROFILE%\vcpkg, or tools\vcpkg.'
}

$winapp = Get-PvWinAppPath
if ($winapp) {
    $winappVersion = Get-VersionLine $winapp @('--help')
    if ($winappVersion -match 'logon session|cannot run|failed to run|StandardOutputEncoding|无法运行') {
        Add-Check 'winapp CLI' 'Warn' ("Found at {0}, but help check failed: {1}" -f $winapp, $winappVersion)
    }
    else {
        Add-Check 'winapp CLI' 'Pass' ("{0} ({1})" -f $winappVersion, $winapp)
    }
}
else {
    Add-Check 'winapp CLI' 'Warn' 'winapp was not found. MSIX packaging can still use MSBuild, but winapp-based workflows will be unavailable.'
}

if ($dotnet) {
    $templateOutput = @()
    $templateExitCode = 0
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $templateOutput = @(& $dotnet new list winui 2>&1)
        $templateExitCode = $LASTEXITCODE
    }
    catch {
        $templateOutput += $_.Exception.Message
        $templateExitCode = 1
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $templateText = $templateOutput -join "`n"
    if ($templateExitCode -eq 0 -and $templateText -match 'WinUI') {
        Add-Check 'WinUI templates' 'Pass' 'dotnet new lists WinUI templates.'
    }
    else {
        Add-Check 'WinUI templates' 'Warn' 'dotnet new list winui did not find WinUI templates.'
    }
}

try {
    $runtimePackages = @(Get-AppxPackage -Name '*appruntime*' -ErrorAction SilentlyContinue)
    $runtimeVersions = @($runtimePackages | ForEach-Object { $_.Version.ToString() })
    $hasRuntime2 = @($runtimePackages | Where-Object {
        $_.Name -match 'AppRuntime.*(\.2|2\.0)' -or $_.Version.ToString() -match '^2\.'
    }).Count -gt 0
    if ($hasRuntime2) {
        $runtimeSummary = @($runtimePackages | ForEach-Object { '{0} {1} {2}' -f $_.Name, $_.Version, $_.Architecture }) -join '; '
        Add-Check 'Windows App SDK Runtime' 'Pass' $runtimeSummary
    }
    elseif ($runtimeVersions.Count -gt 0) {
        $runtimeSummary = @($runtimePackages | ForEach-Object { '{0} {1} {2}' -f $_.Name, $_.Version, $_.Architecture }) -join '; '
        Add-Check 'Windows App SDK Runtime' 'Warn' ('2.x runtime not detected. Installed: ' + $runtimeSummary)
    }
    else {
        Add-Check 'Windows App SDK Runtime' 'Warn' 'No Microsoft.WindowsAppRuntime packages were detected.'
    }
}
catch {
    Add-Check 'Windows App SDK Runtime' 'Warn' $_.Exception.Message
}

Write-PvSection 'Environment'
$checks | Format-Table -AutoSize

$failures = @($checks | Where-Object { $_.Status -eq 'Fail' })
$warnings = @($checks | Where-Object { $_.Status -eq 'Warn' })

if ($failures.Count -gt 0 -or ($Strict -and $warnings.Count -gt 0)) {
    Write-Host ''
    Write-Host ("Environment check failed: {0} failure(s), {1} warning(s)." -f $failures.Count, $warnings.Count) -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host ("Environment check completed: {0} warning(s)." -f $warnings.Count) -ForegroundColor Green
