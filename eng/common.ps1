Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PvRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

function Join-PvPath {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Parts
    )

    $path = Get-PvRoot
    foreach ($part in $Parts) {
        $path = Join-Path $path $part
    }

    return $path
}

function Assert-PvPathInsideRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $root = [System.IO.Path]::GetFullPath((Get-PvRoot).TrimEnd('\') + '\')
    $rootNoSlash = $root.TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($Path)

    if ($full.Equals($rootNoSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full
    }

    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a path outside the repository root: $full"
    }

    return $full
}

function Initialize-PvDirectories {
    $relativeDirs = @(
        'artifacts',
        'artifacts\app',
        'artifacts\native',
        'artifacts\packages',
        'build',
        'build\app',
        'build\native',
        'logs',
        'tools'
    )

    foreach ($relativeDir in $relativeDirs) {
        $target = Assert-PvPathInsideRoot (Join-PvPath $relativeDir)
        if (-not (Test-Path -LiteralPath $target -PathType Container)) {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
        }
    }
}

function Get-PvProgramFilesX86 {
    if (${env:ProgramFiles(x86)}) {
        return ${env:ProgramFiles(x86)}
    }

    return $env:ProgramFiles
}

function Resolve-PvToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [string[]] $Candidates = @()
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Get-PvVsWherePath {
    $programFilesX86 = Get-PvProgramFilesX86
    return Resolve-PvToolPath 'vswhere' @(
        (Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe')
    )
}

function Get-PvVsInstallationPath {
    $vswhere = Get-PvVsWherePath
    if (-not $vswhere) {
        return $null
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        return $null
    }

    return $installPath
}

function Get-PvVsDevCmdPath {
    $vsPath = Get-PvVsInstallationPath
    if (-not $vsPath) {
        return $null
    }

    $devCmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
    if (Test-Path -LiteralPath $devCmd -PathType Leaf) {
        return $devCmd
    }

    return $null
}

function Import-PvVsDevEnvironment {
    param(
        [ValidateSet('x64', 'x86', 'arm64')]
        [string] $Architecture = 'x64'
    )

    $devCmd = Get-PvVsDevCmdPath
    if (-not $devCmd) {
        throw 'Visual Studio Build Tools developer command script was not found.'
    }

    $cmd = "`"$devCmd`" -arch=$Architecture -host_arch=x64 >nul && set"
    $lines = & cmd.exe /s /c $cmd

    foreach ($line in $lines) {
        if ($line -match '^(.*?)=(.*)$') {
            Set-Item -Path ("Env:{0}" -f $matches[1]) -Value $matches[2]
        }
    }
}

function Get-PvMSBuildPath {
    $vsPath = Get-PvVsInstallationPath
    $candidates = @()
    if ($vsPath) {
        $candidates += Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
    }

    return Resolve-PvToolPath 'msbuild' $candidates
}

function Get-PvNinjaPath {
    $vsPath = Get-PvVsInstallationPath
    $candidates = @()
    if ($vsPath) {
        $candidates += Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    }

    return Resolve-PvToolPath 'ninja' $candidates
}

function Get-PvCMakePath {
    return Resolve-PvToolPath 'cmake'
}

function Get-PvDotNetPath {
    return Resolve-PvToolPath 'dotnet'
}

function Get-PvGitPath {
    return Resolve-PvToolPath 'git'
}

function Get-PvPwshPath {
    return Resolve-PvToolPath 'pwsh' @(
        (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'),
        (Join-Path $env:ProgramFiles 'PowerShell\7-preview\pwsh.exe')
    )
}

function Get-PvVcpkgPath {
    return Resolve-PvToolPath 'vcpkg' @(
        'C:\vcpkg\vcpkg.exe',
        (Join-Path $env:USERPROFILE 'vcpkg\vcpkg.exe'),
        (Join-PvPath 'tools' 'vcpkg' 'vcpkg.exe')
    )
}

function Get-PvWinAppPath {
    $npmWinApp = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'npm\winapp.cmd'
    return Resolve-PvToolPath 'winapp.cmd' @(
        $npmWinApp,
        (Resolve-PvToolPath 'winapp')
    )
}

function Get-PvWindowsSdkVersions {
    $programFilesX86 = Get-PvProgramFilesX86
    $sdkLibRoot = Join-Path $programFilesX86 'Windows Kits\10\Lib'
    if (-not (Test-Path -LiteralPath $sdkLibRoot -PathType Container)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $sdkLibRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object Name -Descending |
        ForEach-Object { $_.Name })
}

function Get-PvNativeSourceRoot {
    $candidates = @(
        (Join-PvPath 'CMakeLists.txt'),
        (Join-PvPath 'src' 'CMakeLists.txt'),
        (Join-PvPath 'src' 'PulseView.Core.Native' 'CMakeLists.txt')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Split-Path -Parent $candidate)
        }
    }

    return $null
}

function Get-PvAppProjectPath {
    $expected = Join-PvPath 'src' 'PulseView.App' 'PulseView.App.csproj'
    if (Test-Path -LiteralPath $expected -PathType Leaf) {
        return $expected
    }

    $appDir = Join-PvPath 'src' 'PulseView.App'
    if (Test-Path -LiteralPath $appDir -PathType Container) {
        $project = Get-ChildItem -LiteralPath $appDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($project) {
            return $project.FullName
        }
    }

    return $null
}

function Get-PvProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $PropertyName
    )

    $ProjectPath = Assert-PvPathInsideRoot $ProjectPath
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Project file was not found: $ProjectPath"
    }

    [xml] $projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    foreach ($propertyGroup in @($projectXml.Project.PropertyGroup)) {
        $property = $propertyGroup.SelectSingleNode($PropertyName)
        if ($property -and -not [string]::IsNullOrWhiteSpace($property.InnerText)) {
            return [string] $property.InnerText
        }
    }

    return $null
}

function Get-PvAppRuntimeIdentifier {
    param(
        [ValidateSet('x64', 'x86', 'arm64')]
        [string] $Platform = 'x64'
    )

    return "win-$($Platform.ToLowerInvariant())"
}

function Get-PvAppBuildOutputDirectory {
    param(
        [ValidateSet('Debug', 'Release')]
        [string] $Configuration = 'Debug',

        [ValidateSet('x64', 'x86', 'arm64')]
        [string] $Platform = 'x64'
    )

    $appProject = Get-PvAppProjectPath
    if (-not $appProject) {
        throw 'App project was not found. Expected src\PulseView.App\PulseView.App.csproj.'
    }

    $targetFramework = Get-PvProjectProperty $appProject 'TargetFramework'
    if (-not $targetFramework) {
        throw "TargetFramework was not found in $appProject"
    }

    $runtimeIdentifier = Get-PvAppRuntimeIdentifier -Platform $Platform
    $projectDirectory = Split-Path -Parent $appProject
    $candidates = @(
        (Join-Path $projectDirectory "bin\$Platform\$Configuration\$targetFramework\$runtimeIdentifier"),
        (Join-Path $projectDirectory "bin\$Configuration\$targetFramework\$runtimeIdentifier")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'AppxManifest.xml') -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return [System.IO.Path]::GetFullPath($candidates[0])
}

function Get-PvAppxManifestInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ManifestPath
    )

    $ManifestPath = Assert-PvPathInsideRoot $ManifestPath
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Appx manifest was not found: $ManifestPath"
    }

    [xml] $manifestXml = Get-Content -LiteralPath $ManifestPath -Raw
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifestXml.NameTable)
    $namespaceManager.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')

    $identity = $manifestXml.SelectSingleNode('/m:Package/m:Identity', $namespaceManager)
    $application = $manifestXml.SelectSingleNode('/m:Package/m:Applications/m:Application', $namespaceManager)
    if (-not $identity -or -not $application) {
        throw "Appx manifest is missing Identity or Application metadata: $ManifestPath"
    }

    return [pscustomobject] @{
        IdentityName = $identity.GetAttribute('Name')
        ApplicationId = $application.GetAttribute('Id')
    }
}

function Get-PvInstalledAppPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $IdentityName
    )

    return @(Get-AppxPackage -Name $IdentityName -ErrorAction SilentlyContinue)
}

function Get-PvSolutionOrProjectPath {
    $root = Get-PvRoot
    $solutionX = Get-ChildItem -LiteralPath $root -Filter '*.slnx' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($solutionX) {
        return $solutionX.FullName
    }

    $solution = Get-ChildItem -LiteralPath $root -Filter '*.sln' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($solution) {
        return $solution.FullName
    }

    return Get-PvAppProjectPath
}

function Get-PvManagedTestProjects {
    $src = Join-PvPath 'src'
    if (-not (Test-Path -LiteralPath $src -PathType Container)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.csproj' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\PulseView\..*\.Tests\\' } |
        ForEach-Object { $_.FullName })
}

function Invoke-PvExternal {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-PvRoot)
    )

    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Working directory does not exist: $WorkingDirectory"
    }

    Push-Location $WorkingDirectory
    try {
        Write-Host ("> {0} {1}" -f $FilePath, ($Arguments -join ' '))
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Command failed with exit code $($exitCode): $FilePath"
        }
    }
    finally {
        Pop-Location
    }
}

function Write-PvSection {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Title
    )

    Write-Host ''
    Write-Host "== $Title =="
}
