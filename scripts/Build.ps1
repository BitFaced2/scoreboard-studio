param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location -LiteralPath $taskRoot
try {
    dotnet build ScoreboardStudio.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipChecks) {
        dotnet run --project tests/ScoreboardStudio.Checks -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    }
    dotnet publish src/ScoreboardStudio.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/ScoreboardStudio
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Output 'Ready: artifacts\ScoreboardStudio\ScoreboardStudio.exe'
} finally { Pop-Location }
