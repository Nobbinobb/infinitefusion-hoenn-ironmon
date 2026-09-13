<#
.SYNOPSIS
Builds self-contained Setup and recovery artifacts for the existing release pipeline.
.DESCRIPTION
Consumes generated game inputs and dedicated public trust. Private signing keys
are never read by this build step. Production publication signs the exact frozen
manifest later. Output is restricted to ignored staging and release directories.
#>
param([Parameter(Mandatory)][string]$Version, [string]$HistoryDirectory)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ci/UpdateArtifacts.ps1')
$tool = Join-Path $projectRoot 'tracker/tools/Ironmon.ReleaseTool/bin/Release/net10.0/Ironmon.ReleaseTool.dll'
$trust = Join-Path $projectRoot 'resources/updater/trusted-keys.json'
Invoke-UpdateCandidateTool $tool @('trust', $trust)
$history = if ($HistoryDirectory) { $HistoryDirectory } else { '-' }
$helperVersion = & dotnet $tool helper-version $history $trust
if ($LASTEXITCODE -ne 0 -or $helperVersion -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Cannot determine the next immutable helper version.' }
$stage = Join-Path $projectRoot 'data/updater/release-tools'
if (Test-Path -LiteralPath $stage) { throw 'Updater publish staging already exists; clean the owned staging folder before a new release build.' }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($item in @(
  @{Project='Ironmon.Updater';Directory='helper';Version=$helperVersion;Executable='Ironmon.Updater.exe'},
  @{Project='Ironmon.Setup';Directory='setup';Version=$Version;Executable='Ironmon Setup.exe'}
)) {
  $output = Join-Path $stage $item.Directory
  & dotnet publish (Join-Path $projectRoot "tracker/src/$($item.Project)/$($item.Project).csproj") -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false "-p:Version=$($item.Version)" -o $output
  if ($LASTEXITCODE -ne 0) { throw 'Updater or Setup publication failed.' }
  $file = Join-Path $output $item.Executable
  $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
  if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -cne $item.Version) { throw 'Published component version differs from its release identity.' }
}
$release = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$helper = Join-Path $stage 'helper/Ironmon.Updater.exe'
$archivePath = Join-Path $release "Ironmon-Updater-v$helperVersion.zip"
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
  $entry = $archive.CreateEntry('Ironmon.Updater.exe')
  $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
  $input = [IO.File]::OpenRead($helper)
  $output = $entry.Open()
  try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
} finally { $archive.Dispose(); $stream.Dispose() }
[IO.File]::Copy((Join-Path $stage 'setup/Ironmon Setup.exe'), (Join-Path $release "Ironmon-Setup-v$Version-win-x64.exe"), $false)
foreach ($distribution in 'dist','dist-runtime-required') {
  $tracker = Join-Path $projectRoot "$distribution/Ironmon Tracker/Ironmon Tracker.exe"
  $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($tracker)
  if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -cne $Version) { throw 'Published tracker version differs from Setup and package identity.' }
  $destination = Join-Path $projectRoot "$distribution/Ironmon Tracker/Updater"
  New-Item -ItemType Directory -Path $destination | Out-Null
  Copy-Item -LiteralPath $helper, $trust, (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $destination
}
