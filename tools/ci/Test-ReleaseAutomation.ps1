$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UpstreamInputs.ps1')
. (Join-Path $PSScriptRoot 'Candidate.ps1')
. (Join-Path $PSScriptRoot 'FusionPoolComparison.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("ironmon-automation-contracts-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$script:assertions = 0
function Assert-Equal($Expected, $Actual, [string]$Message) {
  if ($Expected -cne $Actual) { throw "$Message. Expected '$Expected', got '$Actual'." }
  $script:assertions++
}
function Assert-Rejected([scriptblock]$Action, [string]$Message) {
  $rejected = $false
  try { $null = & $Action } catch { $rejected = $true }
  if (-not $rejected) { throw "Expected rejection: $Message." }
  $script:assertions++
}
try {
  $sha = 'a' * 40
  $entries = @([ordered]@{name='settings';sha256='a' * 64}, [ordered]@{name='credits';sha256='b' * 64})
  $fingerprint = Get-UpstreamFingerprint $sha $entries
  $roundTrip = $entries | ConvertTo-Json | ConvertFrom-Json
  Assert-Equal $fingerprint (Get-UpstreamFingerprint $sha $roundTrip) 'Fingerprint must survive JSON serialization'
  Assert-Equal $fingerprint (Get-UpstreamFingerprint $sha @($roundTrip[1], $roundTrip[0])) 'Fingerprint must ignore manifest ordering'
  Assert-Rejected { Assert-Equal $fingerprint (Get-UpstreamFingerprint ('b' * 40) $entries) 'Different game' } 'new game invalidates candidate'
  Assert-Equal 'https://raw.githubusercontent.com/infinitefusion/pif-downloadables/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/CUSTOM_SPRITES' (ConvertTo-FrozenMetadataUrl 'https://raw.githubusercontent.com/infinitefusion/pif-downloadables/refs/heads/master/CUSTOM_SPRITES' $sha) 'Freeze lists to the resolved metadata commit'
  Assert-Equal 'https://infinitefusion.net/customsprites/Sprite_Credits.csv' (ConvertTo-FrozenMetadataUrl 'https://infinitefusion.net/customsprites/Sprite_Credits.csv' $sha) 'Credits remain separately content hashed'
  Assert-Equal 'value' (Get-RubyStringConstant "  URL = 'value' # explanation`r`n" 'URL') 'Parse upstream literal'
  Assert-Rejected { Get-RubyStringConstant "URL = dynamic_method()`n" 'URL' } 'Do not evaluate Ruby to find URLs'
  Assert-Rejected { Get-RubyStringConstant "URL = 'a'`nURL = 'b'`n" 'URL' } 'Ambiguous settings fail'
  $html = Join-Path $testRoot 'bad-download'
  '<html>Temporarily unavailable</html>' | Set-Content $html
  Assert-Rejected { Assert-UpstreamContent 'credits' $html } 'Reject HTML error pages with status 200'
  '123.456.png' | Set-Content $html
  Assert-UpstreamContent 'custom_sprites' $html
  Assert-Rejected { Assert-UpstreamContent 'credits' $html } 'Sprite lists cannot replace credits'

  # Installation must validate the complete snapshot before replacing bundled files.
  $game = Join-Path $testRoot 'game'
  $snapshot = Join-Path $testRoot 'inputs'
  New-Item -ItemType Directory -Path "$game/Data/sprites", "$game/Data/Scripts", $snapshot -Force | Out-Null
  'bundled' | Set-Content "$game/Data/sprites/CUSTOM_SPRITES"
  $specs = @(
    @{name='settings';file='Settings.rb';path='Data/Scripts/DownloadedSettings.rb';text="module Settings`nend"},
    @{name='custom_sprites';file='CUSTOM_SPRITES';path='Data/sprites/CUSTOM_SPRITES';text='1.2.png'},
    @{name='base_sprites';file='BASE_SPRITES';path='Data/sprites/BASE_SPRITES';text='1.png'},
    @{name='credits';file='Sprite_Credits.csv';path='Data/sprites/Sprite_Credits.csv';text='1.2,artist,main'}
  )
  $files = @($specs | ForEach-Object {
    $path = Join-Path $snapshot $_.file
    $_.text | Set-Content $path -Encoding utf8NoBOM
    [ordered]@{name=$_.name;file=$_.file;path=$_.path;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item $path).Length}
  })
  $inputManifest = @{schema_version=1;game_commit=$sha;files=$files;fingerprint=(Get-UpstreamFingerprint $sha $files)}
  $inputManifest | ConvertTo-Json -Depth 8 | Set-Content "$snapshot/upstream-inputs.json"
  # Mock only the local revision read; downloads and game execution never occur in these tests.
  function git { $global:LASTEXITCODE = 0; return ('a' * 40) }
  'corrupt' | Set-Content "$snapshot/Sprite_Credits.csv"
  Assert-Rejected { Install-UpstreamInputs $snapshot $game } 'Any corrupt input prevents installation'
  Assert-Equal 'bundled' (Get-Content "$game/Data/sprites/CUSTOM_SPRITES" -Raw).Trim() 'No partial replacement on validation failure'
  '1.2,artist,main' | Set-Content "$snapshot/Sprite_Credits.csv" -Encoding utf8NoBOM
  $null = Install-UpstreamInputs $snapshot $game
  Assert-Equal '1.2.png' (Get-Content "$game/Data/sprites/CUSTOM_SPRITES" -Raw).Trim() 'Install verified current metadata'
  $inputManifest.files[0].path = '../outside'
  $inputManifest | ConvertTo-Json -Depth 8 | Set-Content "$snapshot/upstream-inputs.json"
  Assert-Rejected { Install-UpstreamInputs $snapshot $game } 'Reject manifest path traversal'
  Remove-Item Function:git

  $candidate = Join-Path $testRoot 'candidate'
  New-Item -ItemType Directory -Path $candidate | Out-Null
  $version = '1.2.3'
  $assets = @("Ironmon-v$version-win-x64.zip", "Ironmon-v$version-win-x64-runtime-required.zip", 'release-evidence.zip') | ForEach-Object {
    $path = Join-Path $candidate $_
    [IO.File]::WriteAllBytes($path, [byte[]]@(1,2,3,4))
    $digest = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  $_" | Set-Content ($path -replace '\.zip$', '.sha256.txt')
    @{name=$_;sha256=$digest;bytes=4}
  }
  $manifest = @{schema_version=1;version=$version;source_commit=$sha;source_tree=$sha;run_id='123';inputs=@{fingerprint=$fingerprint;game_commit=$sha};assets=@($assets)}
  $manifest | ConvertTo-Json -Depth 8 | Set-Content "$candidate/candidate.json"
  $null = Assert-ReleaseCandidate $candidate $sha $fingerprint $version
  $script:assertions++
  Assert-Rejected { Assert-ReleaseCandidate $candidate ('b' * 40) $fingerprint $version } 'Merged source tree must equal validated tree'
  Assert-Rejected { Assert-ReleaseCandidate $candidate $sha ('c' * 64) $version } 'Changed metadata requires a rebuild'
  Assert-Rejected { Assert-ReleaseCandidate $candidate $sha $fingerprint '1.2.4' } 'Candidate must match approved version'
  'extra' | Set-Content "$candidate/unexpected.txt"
  Assert-Rejected { Assert-ReleaseCandidate $candidate $sha $fingerprint $version } 'Unlisted assets cannot be published'
  Remove-Item "$candidate/unexpected.txt"
  'tampered' | Set-Content (Join-Path $candidate $assets[0].name)
  Assert-Rejected { Assert-ReleaseCandidate $candidate $sha $fingerprint $version } 'Tampering fails before publication'

  # Two different species dimensions must compare biological identities, not bit offsets.
  $oldPool = [Text.Encoding]::ASCII.GetBytes('IFCFPOOL') + [BitConverter]::GetBytes([uint16]1) + [BitConverter]::GetBytes([uint16]2) + [BitConverter]::GetBytes([uint32]2) + [BitConverter]::GetBytes([uint32]1) + [byte[]]@(3)
  $newPool = [Text.Encoding]::ASCII.GetBytes('IFCFPOOL') + [BitConverter]::GetBytes([uint16]1) + [BitConverter]::GetBytes([uint16]3) + [BitConverter]::GetBytes([uint32]2) + [BitConverter]::GetBytes([uint32]2) + [byte[]]@(10,0)
  $comparison = Compare-FusionPools $oldPool $newPool
  Assert-Equal 1 $comparison.added_count 'Report new identity across species-count changes'
  Assert-Equal 'B2H1' $comparison.added[0] 'Correct added identity'
  Assert-Equal 'B1H1' $comparison.removed[0] 'Correct removed identity'

  Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | ForEach-Object {
    $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$parseErrors)
    if ($parseErrors) { throw ($parseErrors | Out-String) }
  }
  Write-Output "Release automation contracts passed: $script:assertions assertions; all CI PowerShell scripts parsed."
} finally {
  if (Test-Path Function:git) { Remove-Item Function:git }
  $resolved = [IO.Path]::GetFullPath($testRoot)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
      [IO.Path]::GetFileName($resolved) -notmatch '^ironmon-automation-contracts-[0-9a-f]{32}$') { throw 'Unsafe test cleanup path.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
