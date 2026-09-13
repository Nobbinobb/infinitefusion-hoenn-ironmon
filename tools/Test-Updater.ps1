<#
.SYNOPSIS
Provisions the pinned MinGit test archive and runs updater and native Setup tests.

.DESCRIPTION
Verifies the archive size and SHA-256 digest before use. Missing or damaged
archives are downloaded unless -Offline is selected. Test repositories and
private runtimes are created beneath ignored test output, outside the game.

.PARAMETER Configuration
The build configuration to test. Defaults to Debug.

.PARAMETER Offline
Requires an already provisioned archive and restored dependencies. Disables
package restore and excludes tests that access the network.

.PARAMETER IncludeNetwork
Also runs the production downloader and private Git HTTPS test against the
public game repository. Cannot be combined with -Offline.

.EXAMPLE
./tools/Test-Updater.ps1 -Offline -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Offline,
    [switch]$IncludeNetwork
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'tracker/tests/Ironmon.Updater.Tests/Ironmon.Updater.Tests.csproj'
$assetDirectory = Join-Path $repositoryRoot 'tracker/tests/Ironmon.Updater.Tests/bin/test-assets'
$archivePath = Join-Path $assetDirectory 'MinGit.zip'
$expectedHash = '56d7b226b7693196cfc71fef26568f536c4a021ab6c37ff2db4287bed908e96e'
$expectedSize = 38989688
$assetUrl = 'https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.5/MinGit-2.55.0.5-64-bit.zip'
if ($Offline -and $IncludeNetwork) { throw '-Offline and -IncludeNetwork cannot be combined.' }

function Test-PinnedArchive {
    <#
    .SYNOPSIS
    Checks whether a local archive matches the expected size and SHA-256 digest.

    .PARAMETER Path
    The archive file to verify.

    .OUTPUTS
    System.Boolean. True when the file exists and matches both pinned values.
    #>
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $Path).Length -ne $expectedSize) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $expectedHash
}

if (-not (Test-PinnedArchive -Path $archivePath)) {
    if ($Offline) { throw 'The pinned MinGit test archive is missing or corrupt. Run once without -Offline.' }
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $partialPath = Join-Path $assetDirectory ('download-' + [guid]::NewGuid() + '.zip')
    try {
        Invoke-WebRequest -Uri $assetUrl -OutFile $partialPath
        if (-not (Test-PinnedArchive -Path $partialPath)) { throw 'The MinGit test archive failed verification.' }
        Move-Item -LiteralPath $partialPath -Destination $archivePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $partialPath) { Remove-Item -LiteralPath $partialPath }
    }
}

$testArguments = @('test', $testProject, '--configuration', $Configuration, '--nologo', '-p:UseSharedCompilation=false')
if ($Offline) { $testArguments += '--no-restore' }
if (-not $IncludeNetwork) { $testArguments += @('--filter', 'Category!=Network') }
$testArguments += @('--', 'xUnit.ParallelizeTestCollections=false')
& dotnet @testArguments
if ($LASTEXITCODE -ne 0) { throw "Updater tests failed with exit code $LASTEXITCODE." }

$setupProject = Join-Path $repositoryRoot 'tracker/tests/Ironmon.Setup.Tests/Ironmon.Setup.Tests.csproj'
$setupArguments = @('test', $setupProject, '--configuration', $Configuration, '--nologo', '-p:UseSharedCompilation=false')
if ($Offline) { $setupArguments += '--no-restore' }
& dotnet @setupArguments
if ($LASTEXITCODE -ne 0) { throw "Setup tests failed with exit code $LASTEXITCODE." }
