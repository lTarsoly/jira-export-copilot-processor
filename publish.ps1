param(
    [string]$Project = "JiraExportCopilotProcessor.csproj",
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "publish",
    [switch]$Trimmed,
    [switch]$FrameworkDependent,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $Project -PathType Leaf)) {
    throw "Project file not found: $Project"
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($Project)
$outputDir = Join-Path -Path $OutputRoot -ChildPath "$Framework/$Runtime"
$zipPath = Join-Path -Path $OutputRoot -ChildPath "$projectName-$Framework-$Runtime.zip"
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
$publishTrimmed = if ($Trimmed) { "true" } else { "false" }

Write-Host "Publishing project..." -ForegroundColor Cyan
Write-Host "  Project        : $Project"
Write-Host "  Configuration  : $Configuration"
Write-Host "  Framework      : $Framework"
Write-Host "  Runtime        : $Runtime"
Write-Host "  Output         : $outputDir"
Write-Host "  Self-contained : $selfContained"
Write-Host "  Trimmed        : $publishTrimmed"

if (-not $NoRestore) {
    dotnet restore $Project
}

$publishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "-f", $Framework,
    "-r", $Runtime,
    "-o", $outputDir,
    "--self-contained", $selfContained,
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:PublishTrimmed=$publishTrimmed",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

if ($NoRestore) {
    $publishArgs += "--no-restore"
}

Write-Host ""
Write-Host "Running: dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
Write-Host ""

dotnet @publishArgs

$exePath = Join-Path -Path $outputDir -ChildPath "$projectName.exe"
Write-Host ""
if (Test-Path -Path $exePath -PathType Leaf) {
    Write-Host "Publish completed." -ForegroundColor Green
    Write-Host "Executable: $exePath"

    if (Test-Path -Path $zipPath -PathType Leaf) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path -Path $outputDir -ChildPath "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Archive   : $zipPath"
} else {
    Write-Host "Publish finished, but executable was not found at expected path:" -ForegroundColor Yellow
    Write-Host "  $exePath"
    Write-Host "Check output folder: $outputDir"
}
