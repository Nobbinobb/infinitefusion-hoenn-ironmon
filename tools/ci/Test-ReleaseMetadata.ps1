param([string]$BaseCommit)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectPath = 'tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj'
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot $projectPath) -Raw
$properties = $project.Project.PropertyGroup
$version = [string]$properties.ApplicationDisplayVersion
if ($version -notmatch '^\d+\.\d+\.\d+$' -or [string]$properties.Version -ne $version -or
    [string]$properties.ApplicationVersion -notmatch '^[1-9]\d*$') {
  throw 'Release versions must be consistent: ApplicationDisplayVersion, Version and numeric ApplicationVersion.'
}
. (Join-Path $PSScriptRoot 'UpstreamInputs.ps1')
$rubyVersion = Get-RubyStringConstant (Get-Content (Join-Path $projectRoot 'src/foundation/Core.rb') -Raw) 'VERSION'
if ($rubyVersion -ne $version) { throw 'The mod and tracker versions differ.' }
$notes = Join-Path $projectRoot "docs/releases/RELEASE_NOTES_$version.md"
if (-not (Test-Path -LiteralPath $notes) -or (Get-Content -LiteralPath $notes -Raw) -notmatch "(?m)^# Ironmon $([regex]::Escape($version))\s*$") {
  throw "Release notes for $version are missing or have the wrong heading."
}
$isRelease = $true
if ($BaseCommit) {
  if ($BaseCommit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid release base revision.' }
  [xml]$previous = git -C $projectRoot show "${BaseCommit}:$projectPath"
  if ($LASTEXITCODE -ne 0) { throw 'Cannot read base version.' }
  $oldVersion = [string]$previous.Project.PropertyGroup.ApplicationDisplayVersion
  $isRelease = $oldVersion -ne $version
  if ($isRelease -and ([version]$version -le [version]$oldVersion -or
      [long]$properties.ApplicationVersion -le [long]$previous.Project.PropertyGroup.ApplicationVersion)) {
    throw 'A release must increase both the display version and ApplicationVersion.'
  }
}
if ($env:GITHUB_OUTPUT) {
  "release=$($isRelease.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
  "version=$version" >> $env:GITHUB_OUTPUT
}
Write-Output "Version $version; release change: $isRelease."

