<#
.SYNOPSIS
Checks encrypted icon display, animation, acknowledgement and harmless previews in the bundled game.
#>
param(
    [string]$GameRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'generation/GameRuntime-Tooling.ps1')
$validationRoot = Join-Path $projectRoot 'data/achievement-toast-validation'
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Distribution.ps1') -GameRoot $GameRoot -DistributionRoot (Join-Path $projectRoot 'data/achievement-dist') -ReuseGeneratedAssets
}
$pack = Get-Content -LiteralPath (Join-Path $projectRoot 'data/achievement-icons.pack') -Raw | ConvertFrom-Json
$sealed = $pack.Icons | Where-Object Id -EQ $pack.DefaultId
$context = [Text.Encoding]::UTF8.GetBytes('achievement-icons-v1:' + $pack.DefaultId)
$key = [Security.Cryptography.HMACSHA256]::HashData([Convert]::FromHexString('93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239'), $context)
$ciphertext = [Convert]::FromBase64String($sealed.Ciphertext)
$plaintext = [byte[]]::new($ciphertext.Length)
$cipher = [Security.Cryptography.AesGcm]::new($key, 16)
try {
    $cipher.Decrypt([Convert]::FromBase64String($sealed.Nonce), $ciphertext, [Convert]::FromBase64String($sealed.Tag), $plaintext, $context)
    $image = [Text.Encoding]::UTF8.GetString($plaintext) | ConvertFrom-Json
}
finally { $cipher.Dispose(); [Security.Cryptography.CryptographicOperations]::ZeroMemory($plaintext) }
$inputStream = [IO.MemoryStream]::new([Convert]::FromBase64String($image.Pixels))
$inflater = [IO.Compression.ZLibStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
$pixels = [byte[]]::new($image.Width * $image.Height * 4)
try { $inflater.ReadExactly($pixels) } finally { $inflater.Dispose(); $inputStream.Dispose() }
$fixturePath = Join-Path $validationRoot 'icon-fixture.json'
[IO.File]::WriteAllText($fixturePath, (@{ width=$image.Width; height=$image.Height; pixels=[Convert]::ToBase64String($pixels) } | ConvertTo-Json -Compress))
$resultPath = Join-Path $validationRoot 'runtime.tests.json'
$errorPath = Join-Path $validationRoot 'runtime.error'
$capturePath = Join-Path $validationRoot 'native-toast.png'
Remove-Item -LiteralPath $resultPath,$errorPath -Force -ErrorAction SilentlyContinue
$loader = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'generation/Script-Loader.rb'))
$test = [IO.File]::ReadAllText((Join-Path $projectRoot 'tests/runtime/Achievements.rb'))
$source = @(
    'begin'
    $loader
    'IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:998|999)/])'
    ('$ironmon_achievement_icon_fixture = JSON.parse(File.binread({0})).transform_keys(&:to_s)' -f ($fixturePath.Replace('\','/') | ConvertTo-Json -Compress))
    ('$ironmon_achievement_capture_path = {0}' -f ($capturePath.Replace('\','/') | ConvertTo-Json -Compress))
    $test
    ('File.binwrite({0}, JSON.generate(IronmonAchievementRuntimeTests.run))' -f ($resultPath.Replace('\','/') | ConvertTo-Json -Compress))
    'rescue Exception => error'
    ('File.binwrite({0}, [error.class.to_s, error.message, *error.backtrace].join("\n"))' -f ($errorPath.Replace('\','/') | ConvertTo-Json -Compress))
    'exit!(1)'
    'end'
    'exit!(0)'
) -join "`n"
Invoke-IronmonGameRuntime -GameRoot $GameRoot -RubySource $source -TimeoutSeconds 180 -OperationName 'achievement icon and notification tests' -ErrorReportPath $errorPath
Get-Content -LiteralPath $resultPath
Write-Host "Native render: $capturePath"
