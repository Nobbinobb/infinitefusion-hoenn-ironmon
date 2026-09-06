param(
  [string]$ProjectRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
  [string]$AssetDirectory
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (-not $AssetDirectory) { $AssetDirectory = Join-Path $ProjectRoot 'release' }
[xml]$project = Get-Content (Join-Path $ProjectRoot 'tracker/src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.ApplicationDisplayVersion
$sourceManifest = Get-Content (Join-Path $ProjectRoot 'src/load_order.json') -Raw | ConvertFrom-Json
$receipts = @()
$sharedData = $null
foreach ($name in "Ironmon-v$version-win-x64.zip", "Ironmon-v$version-win-x64-runtime-required.zip") {
  $path = Join-Path $AssetDirectory $name
  $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ((Get-Content ($path -replace '\.zip$', '.sha256.txt') -Raw).Trim() -cne "$hash  $name") { throw 'Archive checksum sidecar mismatch.' }
  $zip = [IO.Compression.ZipFile]::OpenRead($path)
  try {
    $files = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $names = @($files.FullName)
    if (@($names | Sort-Object -Unique).Count -ne $names.Count) { throw 'Duplicate archive paths.' }
    foreach ($required in 'README.md', 'INSTALLATION.md', 'RELEASE_NOTES.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'OPEN-SANS-LICENSE.txt', 'Ironmon Tracker/Ironmon Tracker.exe') {
      if ($required -cnotin $names) { throw "Missing release file: $required." }
    }
    $notesReader = [IO.StreamReader]::new($zip.GetEntry('RELEASE_NOTES.md').Open())
    try { $packagedNotes = $notesReader.ReadToEnd().Replace("`r`n", "`n") } finally { $notesReader.Dispose() }
    $expectedNotes = [IO.File]::ReadAllText((Join-Path $ProjectRoot "docs/releases/RELEASE_NOTES_$version.md")).Replace("`r`n", "`n")
    if ($packagedNotes -cne $expectedNotes) { throw 'The package contains release notes for different source or version.' }
    if ($names | Where-Object { $_ -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$)|(^|/)(\.git|\.env)(/|$)|AccessGenerator|private[-_ ]?key|998_Ironmon_Development|maintainer-dist|\.(ironmon-access|p8|p12|pfx|pem|key|tests|progress|bootstrap|tmp)$)' }) {
      throw 'Unsafe, maintainer or sensitive file in player archive.'
    }
    $data = @{}
    foreach ($entry in $files) {
      $stream = $entry.Open()
      $sha = [Security.Cryptography.SHA256]::Create()
      try { $digest = [Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() } finally { $sha.Dispose(); $stream.Dispose() }
      if ($entry.FullName.StartsWith('Data/')) { $data[$entry.FullName] = $digest }
    }
    $rubyFiles = @($names | Where-Object { $_ -match '^Data/Scripts/997_Ironmon/.*\.rb$' })
    if ($rubyFiles.Count -ne $sourceManifest.Count) { throw 'Runtime script count mismatch.' }
    foreach ($script in $sourceManifest) {
      $entry = $zip.GetEntry("Data/Scripts/997_Ironmon/$($script.output)")
      if (-not $entry) { throw "Missing runtime script: $($script.output)." }
      $reader = [IO.StreamReader]::new($entry.Open())
      try { $actual = $reader.ReadToEnd().Replace("`r`n", "`n") } finally { $reader.Dispose() }
      $expected = [IO.File]::ReadAllText((Join-Path $ProjectRoot "src/$($script.source)")).Replace("`r`n", "`n")
      if ($actual -cne $expected) { throw "Packaged Ruby differs from source: $($script.source)." }
    }
    if ($null -ne $sharedData) {
      if ($sharedData.Count -ne $data.Count) { throw 'Shared Data counts differ.' }
      foreach ($key in $sharedData.Keys) { if ($data[$key] -cne $sharedData[$key]) { throw "Shared Data differs: $key." } }
    } else { $sharedData = $data }
    $receipts += [ordered]@{name=$name;bytes=(Get-Item $path).Length;sha256=$hash;files=$files.Count;ruby_scripts=$rubyFiles.Count;shared_data_files=$data.Count}
  } finally { $zip.Dispose() }
}
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot 'data') | Out-Null
$receipts | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ProjectRoot 'data/release-archives.audit.json') -Encoding utf8NoBOM
Write-Output 'Both player archives verified: all entries readable, checksums, source scripts, required documents, sensitive-file exclusions and shared Data parity.'
