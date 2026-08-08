$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$distributionRoot = Join-Path $projectRoot "dist"
$output = Join-Path $distributionRoot "Ironmon Tracker"
$project = Join-Path $projectRoot "tracker\src\Ironmon.Tracker.App\Ironmon.Tracker.App.csproj"
$resolvedDistribution = [System.IO.Path]::GetFullPath($distributionRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($output)
if (!$resolvedOutput.StartsWith($resolvedDistribution + [System.IO.Path]::DirectorySeparatorChar)) {
  throw "Tracker publish output must remain inside the distribution directory."
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
  throw "Tracker publication failed with exit code $LASTEXITCODE."
}

$winUiResourceFiles = @(
  "Microsoft.ui.xaml.dll.mui",
  "Microsoft.UI.Xaml.Phone.dll.mui"
)
$winUiCultureDirectories = Get-ChildItem -LiteralPath $resolvedOutput -Directory |
  Where-Object {
    $cultureDirectory = $_
    $cultureDirectory.Name -ne "en-us" -and
    ($winUiResourceFiles | Where-Object {
      Test-Path -LiteralPath (Join-Path $cultureDirectory.FullName $_)
    })
  }
$winUiCultureDirectories | Remove-Item -Recurse -Force

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

Write-Output "Published self-contained tracker to $resolvedOutput"
