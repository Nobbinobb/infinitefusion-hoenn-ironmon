$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$distributionRoot = Join-Path $projectRoot "maintainer-dist"
$output = Join-Path $distributionRoot "Ironmon Access Token Generator"
$project = Join-Path $projectRoot "tracker\tools\Ironmon.Tracker.AccessGenerator.App\Ironmon.Tracker.AccessGenerator.App.csproj"
$resolvedDistribution = [System.IO.Path]::GetFullPath($distributionRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($output)
if (!$resolvedOutput.StartsWith($resolvedDistribution + [System.IO.Path]::DirectorySeparatorChar)) {
  throw "Generator publish output must remain inside the maintainer distribution directory."
}

[xml]$projectDocument = Get-Content -LiteralPath $project
$supportedCultureProperty = @($projectDocument.Project.PropertyGroup.SatelliteResourceLanguages) |
  Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
  Select-Object -First 1
if ($null -eq $supportedCultureProperty) {
  throw "The generator project must define SatelliteResourceLanguages."
}
$supportedCultures = @($supportedCultureProperty -split ";") |
  ForEach-Object { $_.Trim().ToLowerInvariant() } |
  Where-Object { $_.Length -gt 0 }
if ($supportedCultures.Count -eq 0) {
  throw "The generator project must support at least one satellite-resource culture."
}

if (Test-Path -LiteralPath $resolvedOutput) {
  Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

& dotnet publish $project `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output $resolvedOutput `
  --no-restore `
  --nologo `
  --disable-build-servers `
  -m:1 `
  -p:UseSharedCompilation=false `
  -p:PublishReadyToRun=false `
  -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
  throw "Generator publication failed with exit code $LASTEXITCODE."
}

$unsupportedCultureDirectories = Get-ChildItem -LiteralPath $resolvedOutput -Directory |
  Where-Object {
    try {
      [System.Globalization.CultureInfo]::GetCultureInfo($_.Name) | Out-Null
      $supportedCultures -notcontains $_.Name.ToLowerInvariant()
    }
    catch [System.Globalization.CultureNotFoundException] {
      $false
    }
  }
foreach ($cultureDirectory in $unsupportedCultureDirectories) {
  $resolvedCultureDirectory = [System.IO.Path]::GetFullPath($cultureDirectory.FullName)
  if (!$resolvedCultureDirectory.StartsWith($resolvedOutput + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "Culture directory must remain inside the generator publish output."
  }

  Remove-Item -LiteralPath $resolvedCultureDirectory -Recurse -Force
}

Get-ChildItem -LiteralPath $resolvedOutput -Filter "*.pdb" -File |
  Remove-Item -Force

$documentationFiles = Get-ChildItem -LiteralPath $resolvedOutput -Filter "*.xml" -File -Recurse |
  Where-Object {
    $assembly = [System.IO.Path]::ChangeExtension($_.FullName, ".dll")
    Test-Path -LiteralPath $assembly
  }
$documentationFiles | Remove-Item -Force

$privateMaterialPatterns = @("*.pem", "*.key", "*.pfx", "*.p12", "*.jwk", "*.jwkset", "*.ironmon-access")
$privateMaterial = foreach ($pattern in $privateMaterialPatterns) {
  Get-ChildItem -LiteralPath $resolvedOutput -Filter $pattern -File -Recurse
}
if (@($privateMaterial).Count -gt 0) {
  throw "Generator publication contains private key or generated access-token material."
}

$executable = Join-Path $resolvedOutput "Ironmon.AG.exe"
if (!(Test-Path -LiteralPath $executable)) {
  throw "Generator publication did not create Ironmon.AG.exe."
}

Write-Output "Published self-contained access-token generator to $resolvedOutput"
