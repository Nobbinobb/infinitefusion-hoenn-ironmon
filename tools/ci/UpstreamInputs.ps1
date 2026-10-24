# Shared by the read-only resolver, installer and offline contract tests.
function Get-UpstreamRevision([string]$Repository, [string]$Branch) {
  $line = git ls-remote "https://github.com/$Repository.git" "refs/heads/$Branch"
  if ($LASTEXITCODE -ne 0 -or $line -notmatch '^([0-9a-f]{40})\s') {
    throw "Cannot resolve newest $Repository/$Branch."
  }
  return $Matches[1]
}

function Get-RubyStringConstant([string]$Text, [string]$Name) {
  $pattern = '(?m)^\s*' + [regex]::Escape($Name) + '\s*=\s*["'']([^"''\r\n]+)["'']\s*(?:#.*)?$'
  $matches = [regex]::Matches($Text, $pattern)
  if ($matches.Count -ne 1) { throw "Expected one literal upstream setting: $Name."
  }
  return $matches[0].Groups[1].Value
}

function ConvertTo-FrozenMetadataUrl([string]$Url, [string]$Revision) {
  if ($Revision -notmatch '^[0-9a-f]{40}$') { throw 'Invalid metadata revision.' }
  return $Url -replace '(?<=https://raw\.githubusercontent\.com/infinitefusion/pif-downloadables/)(?:refs/heads/)?master/', "$Revision/"
}

function Save-UpstreamFile([string]$Url, [string]$Path) {
  $uri = [uri]$Url
  if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('raw.githubusercontent.com', 'infinitefusion.net') -or $uri.UserInfo) {
    throw "Unsupported upstream metadata origin: $Url."
  }
  Invoke-WebRequest -Uri $uri -OutFile $Path -TimeoutSec 180 -MaximumRetryCount 2 -RetryIntervalSec 3
  if ((Get-Item -LiteralPath $Path).Length -eq 0) { throw "Empty upstream download: $Url."
  }
}

function Assert-UpstreamContent([string]$Name, [string]$Path) {
  $text = Get-Content -LiteralPath $Path -Raw
  if ($text -match '(?is)^\s*<(?:!doctype|html)') { throw "HTML returned for $Name."
  }
  $pattern = switch ($Name) {
    'settings' { '(?m)^module Settings\s*$' }
    'custom_sprites' { '(?m)^\d+\.\d+[a-zA-Z]*\.png\r?$' }
    'base_sprites' { '(?m)^\d+[a-zA-Z]*\.png\r?$' }
    'credits' { '(?mi)^\d+\.\d+[a-zA-Z]*,[^,\r\n]+,(?:main|temp)(?:,|\r?$)' }
    default { throw "Unknown upstream input $Name."
    }
  }
  if ($text -notmatch $pattern) { throw "Invalid upstream $Name content."
  }
}

function Get-UpstreamFingerprint([string]$GameCommit, $Files) {
  $lines = @($GameCommit) + @($Files | ForEach-Object { "$($_.name):$($_.sha256)" } | Sort-Object)
  $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
  return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Install-UpstreamInputs([string]$SnapshotDirectory, [string]$GameRoot) {
  $manifest = Get-Content -LiteralPath (Join-Path $SnapshotDirectory 'upstream-inputs.json') -Raw | ConvertFrom-Json
  $gameCommit = git -C $GameRoot rev-parse HEAD
  if ($LASTEXITCODE -ne 0 -or $gameCommit -ne $manifest.game_commit -or $manifest.schema_version -ne 1) {
    throw 'Input snapshot does not match the downloaded game revision.'
  }
  $destinations = @{
    settings = 'Data/Scripts/DownloadedSettings.rb'; custom_sprites = 'Data/sprites/CUSTOM_SPRITES'
    base_sprites = 'Data/sprites/BASE_SPRITES'; credits = 'Data/sprites/Sprite_Credits.csv'
  }
  if (@($manifest.files).Count -ne 4 -or @($manifest.files.name | Sort-Object -Unique).Count -ne 4 -or
      (Get-UpstreamFingerprint $gameCommit $manifest.files) -ne $manifest.fingerprint) {
    throw 'Input snapshot manifest is invalid.'
  }
  # Validate every download before replacing any bundled input. Each generator
  # starts a fresh runtime/Game_Temp, so no previously loaded sprite cache survives.
  foreach ($file in $manifest.files) {
    $expectedFile = if ($file.name -eq 'settings') { 'Settings.rb' } else { [IO.Path]::GetFileName($file.path) }
    if (-not $destinations.ContainsKey($file.name) -or $file.path -ne $destinations[$file.name] -or
        $file.file -cne $expectedFile) { throw 'Unsafe input snapshot path.' }
  }
  foreach ($file in $manifest.files) {
    $path = Join-Path $SnapshotDirectory $file.file
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
      throw "Input snapshot checksum mismatch: $($file.name)."
    }
    Assert-UpstreamContent $file.name $path
  }
  foreach ($file in $manifest.files) {
    $destination = Join-Path $GameRoot $file.path
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $SnapshotDirectory $file.file) -Destination $destination -Force
  }
  return $manifest
}

function Assert-IronmonReleaseMetadata {
  <#
  .SYNOPSIS
  Validates the actual source version and release notes before a build or publication.
  #>
  param([string]$BaseCommit)
  $ErrorActionPreference = 'Stop'
  $PSNativeCommandUseErrorActionPreference = $true
  $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  $projectPath = 'tracker/src/Tracker/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj'
  $legacyProjectPath = 'tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj'
  [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot $projectPath) -Raw
  $properties = $project.Project.PropertyGroup
  $version = [string]$properties.ApplicationDisplayVersion
  if ($version -notmatch '^\d+\.\d+\.\d+$' -or [string]$properties.Version -ne $version -or
      [string]$properties.ApplicationVersion -notmatch '^[1-9]\d*$') {
    throw 'Release versions must be consistent: ApplicationDisplayVersion, Version and numeric ApplicationVersion.'
  }
  $rubyVersion = Get-RubyStringConstant (Get-Content (Join-Path $projectRoot 'src/foundation/Core.rb') -Raw) 'VERSION'
  if ($rubyVersion -ne $version) { throw 'The mod and tracker versions differ.' }
  $notes = Join-Path $projectRoot "docs/releases/RELEASE_NOTES_$version.md"
  if (-not (Test-Path -LiteralPath $notes) -or (Get-Content -LiteralPath $notes -Raw) -notmatch "(?m)^# Ironmon $([regex]::Escape($version))\s*$") {
    throw "Release notes for $version are missing or have the wrong heading."
  }
  $isRelease = $true
  if ($BaseCommit) {
    if ($BaseCommit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid release base revision.' }
    $baseProjectPath = git -C $projectRoot ls-tree --name-only $BaseCommit -- $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect base version.' }
    if (-not $baseProjectPath) {
      $baseProjectPath = git -C $projectRoot ls-tree --name-only $BaseCommit -- $legacyProjectPath
      if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect legacy base version.' }
    }
    if ($baseProjectPath -cnotin @($projectPath, $legacyProjectPath)) { throw 'Cannot find tracker project at base revision.' }
    [xml]$previous = git -C $projectRoot show "${BaseCommit}:$baseProjectPath"
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

}
