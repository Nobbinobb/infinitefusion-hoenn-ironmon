param(
  [ValidateSet("SelfContained", "RuntimeRequired")]
  [string]$DeploymentMode = "SelfContained",
  [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$distributionRootName = if ($DeploymentMode -eq "SelfContained") { "dist" } else { "dist-runtime-required" }
$distributionRoot = Join-Path $projectRoot $distributionRootName
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  Join-Path $distributionRoot "Ironmon Tracker"
} elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
  $OutputDirectory
} else {
  Join-Path $projectRoot $OutputDirectory
}
$project = Join-Path $projectRoot "tracker\src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj"
$resolvedOutput = [System.IO.Path]::GetFullPath($output)
$allowedDistributionRoots = "dist", "dist-runtime-required" | ForEach-Object {
  [System.IO.Path]::GetFullPath((Join-Path $projectRoot $_)).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
}
$outputIsAllowed = $allowedDistributionRoots | Where-Object {
  $resolvedOutput.StartsWith($_ + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}
if (!$outputIsAllowed) {
  throw "Tracker publish output must remain inside a tracker distribution directory."
}
$selfContained = $DeploymentMode -eq "SelfContained"
$selfContainedArgument = $selfContained.ToString().ToLowerInvariant()

[xml]$projectDocument = Get-Content -LiteralPath $project
$supportedCultureProperty = @($projectDocument.Project.PropertyGroup.SatelliteResourceLanguages) |
  Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
  Select-Object -First 1
if ($null -eq $supportedCultureProperty) {
  throw "The tracker project must define SatelliteResourceLanguages."
}
$supportedCultures = @($supportedCultureProperty -split ";") |
  ForEach-Object { $_.Trim().ToLowerInvariant() } |
  Where-Object { $_.Length -gt 0 }
if ($supportedCultures.Count -eq 0) {
  throw "The tracker project must support at least one satellite-resource culture."
}

if (Test-Path -LiteralPath $resolvedOutput) {
  Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

& dotnet publish $project `
  --configuration Release `
  --runtime win-x64 `
  --self-contained $selfContainedArgument `
  --output $resolvedOutput `
  --no-restore `
  --nologo `
  --disable-build-servers `
  -m:1 `
  -p:UseSharedCompilation=false `
  -p:PublishReadyToRun=false `
  -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
  throw "Tracker publication failed with exit code $LASTEXITCODE."
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
    throw "Culture directory must remain inside the tracker publish output."
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

$executable = Join-Path $resolvedOutput "Ironmon Tracker.exe"
if (!(Test-Path -LiteralPath $executable)) {
  throw "Tracker publication did not create Ironmon Tracker.exe."
}

$deploymentLabel = if ($selfContained) { "self-contained" } else { "runtime-required" }
Write-Output "Published $deploymentLabel tracker to $resolvedOutput"
