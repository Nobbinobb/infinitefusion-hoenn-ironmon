$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
  throw 'Hosted game setup is restricted to a disposable Windows Actions runner.'
}
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameRoot = Split-Path -Parent $projectRoot
$gameCommit = git -C $gameRoot rev-parse HEAD
if ($gameCommit -notmatch '^[0-9a-f]{40}$') { throw 'Downloaded game revision is invalid.' }
$settings = Get-Content (Join-Path $gameRoot 'Data/Scripts/001_Settings.rb') -Raw
$versionMatch = [regex]::Match($settings, '(?m)^\s*GAME_VERSION_NUMBER\s*=\s*["''](?<version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)["'']')
if (-not $versionMatch.Success) { throw 'Downloaded game version is unavailable.' }
$gameVersion = $versionMatch.Groups['version'].Value
"GAME_REVISION=$gameCommit" >> $env:GITHUB_ENV
"IRONMON_EXPECTED_GAME_VERSION=$gameVersion" >> $env:GITHUB_ENV
"game_commit=$gameCommit" >> $env:GITHUB_OUTPUT
"game_version=$gameVersion" >> $env:GITHUB_OUTPUT
Write-Output "Using newest Hoenn release: $gameCommit (game version $gameVersion)."
$mesaArchive = Join-Path $env:RUNNER_TEMP 'mesa3d-26.2.0-release-msvc.7z'
$mesaRoot = Join-Path $env:RUNNER_TEMP 'ironmon-mesa'
$mesaDigest = 'dcb2719ef346dab5b609fcb193a5f13cfc4b0502e3f4de1ad43d349477402f47'
Invoke-WebRequest 'https://github.com/pal1000/mesa-dist-win/releases/download/26.2.0/mesa3d-26.2.0-release-msvc.7z' -OutFile $mesaArchive
if ((Get-FileHash $mesaArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $mesaDigest) {
  throw 'Mesa download checksum mismatch.'
}
7z x $mesaArchive "-o$mesaRoot" -y | Out-Null
$mesaLibraries = @(Get-ChildItem (Join-Path $mesaRoot 'x64') -Filter '*.dll' -File)
if (-not ($mesaLibraries | Where-Object Name -eq 'opengl32.dll')) { throw 'Mesa OpenGL library is missing.' }
foreach ($library in $mesaLibraries) {
  $destination = Join-Path $gameRoot $library.Name
  if (Test-Path -LiteralPath $destination) { throw "Refusing to replace bundled game library $($library.Name)." }
  Copy-Item -LiteralPath $library.FullName -Destination $destination
}
'GALLIUM_DRIVER=llvmpipe' >> $env:GITHUB_ENV
'LIBGL_ALWAYS_SOFTWARE=true' >> $env:GITHUB_ENV
'ALSOFT_DRIVERS=null' >> $env:GITHUB_ENV
Write-Output "Prepared pinned Mesa software rendering ($mesaDigest); no system-wide installation."
