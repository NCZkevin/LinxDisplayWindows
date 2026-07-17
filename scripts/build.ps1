param(
  [ValidateSet("win-x64", "win-arm64")]
  [string]$Runtime = "win-x64",

  [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\CodexLinxDisplay.Windows\CodexLinxDisplay.Windows.csproj"
$selfContained = -not $FrameworkDependent
$outputName = if ($FrameworkDependent) {
  "windows-$($Runtime.Substring(4))-framework-dependent"
} else {
  "windows-$($Runtime.Substring(4))"
}
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $outputName))
if (-not $outputPath.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Build output escaped the artifacts directory."
}
if (Test-Path -LiteralPath $outputPath) {
  Remove-Item -LiteralPath $outputPath -Recurse -Force
}

dotnet publish $projectPath `
  --configuration Release `
  --runtime $Runtime `
  --self-contained $selfContained `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=$selfContained `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  --output $outputPath

Write-Host "Windows build created at $outputPath"
