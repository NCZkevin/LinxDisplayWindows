param(
  [ValidateSet("win-x64", "win-arm64")]
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\CodexLinxDisplay.Windows\CodexLinxDisplay.Windows.csproj"
$outputPath = Join-Path $repositoryRoot "artifacts\windows-$($Runtime.Substring(4))"

dotnet publish $projectPath `
  --configuration Release `
  --runtime $Runtime `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output $outputPath

Write-Host "Windows build created at $outputPath"
