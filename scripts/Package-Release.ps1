param([switch]$VerifyReferencePhoto, [switch]$VerifyNdi, [string]$UserGuide)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location -LiteralPath $taskRoot
try {
    [xml]$taskProject = Get-Content -LiteralPath 'src/ScoreboardStudio.App/ScoreboardStudio.App.csproj'
    $taskVersion = [string]$taskProject.Project.PropertyGroup.Version
    if ($taskVersion -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') { throw 'Invalid application version.' }
    $taskName = "ScoreboardStudio-$taskVersion-win-x64"
    $taskRun = Join-Path $taskRoot ('artifacts/release-builds/' + $taskVersion + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $taskPublish = Join-Path $taskRun 'publish'
    $taskPackage = Join-Path $taskRun $taskName
    $taskAssets = Join-Path $taskRun 'assets'
    New-Item -ItemType Directory -Path $taskPackage, $taskAssets -Force | Out-Null
    if ($VerifyReferencePhoto -and -not (Test-Path -LiteralPath 'samples/real-scoreboard/scoreboard.png')) { throw 'Reference photo is missing.' }
    if ($UserGuide -and -not (Test-Path -LiteralPath $UserGuide -PathType Leaf)) { throw 'User guide is missing.' }
    dotnet build ScoreboardStudio.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $taskCheckArgs = @('run', '--project', 'tests/ScoreboardStudio.Checks', '-c', 'Release', '--no-build')
    $taskCheckArgs += @('--', '--update-exe', 'src/ScoreboardStudio.App/bin/Release/net8.0-windows/ScoreboardStudio.exe')
    if ($VerifyNdi) { $taskCheckArgs += '--ndi' }
    if ($VerifyReferencePhoto) { $taskCheckArgs += @('--image', 'samples/real-scoreboard/scoreboard.png', 'samples/real-scoreboard/real-scoreboard.scoreboard.json', (Join-Path $taskRun 'image-checks')) }
    & dotnet @taskCheckArgs
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    dotnet publish src/ScoreboardStudio.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $taskPublish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    # Include known user-facing files only, never debug symbols or build folders.
    Copy-Item -LiteralPath (Join-Path $taskPublish 'ScoreboardStudio.exe') -Destination $taskPackage
    Copy-Item -LiteralPath 'README.md', 'distribution/START-HERE.txt' -Destination $taskPackage
    if ($UserGuide) { Copy-Item -LiteralPath $UserGuide -Destination (Join-Path $taskPackage 'TMS Scoreboard Studio - User Guide.pdf') }

    # Preserve notices for the actual .NET runtimes embedded in this executable.
    $taskConfig = Get-Content -LiteralPath 'src/ScoreboardStudio.App/bin/Release/net8.0-windows/win-x64/ScoreboardStudio.runtimeconfig.json' -Raw | ConvertFrom-Json
    $taskRestore = Get-Content -LiteralPath 'src/ScoreboardStudio.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    foreach ($taskFramework in $taskConfig.runtimeOptions.includedFrameworks) {
        $taskRuntimeName = $taskFramework.name.ToLowerInvariant() + '.runtime.win-x64'
        $taskRuntimeDir = $null
        foreach ($taskCache in $taskRestore.packageFolders.PSObject.Properties.Name) {
            $taskCandidate = Join-Path $taskCache ($taskRuntimeName + '/' + $taskFramework.version)
            if (Test-Path -LiteralPath $taskCandidate) { $taskRuntimeDir = $taskCandidate; break }
        }
        if (-not $taskRuntimeDir) { throw "Runtime notices not found for $($taskFramework.name)." }
        $taskNotices = Join-Path $taskPackage ('notices/' + $taskFramework.name)
        New-Item -ItemType Directory -Path $taskNotices -Force | Out-Null
        $taskLicenseFiles = @(Get-ChildItem -LiteralPath $taskRuntimeDir -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
        if ($taskLicenseFiles.Count -eq 0) { throw "Runtime license not found for $($taskFramework.name)." }
        foreach ($taskLicenseFile in $taskLicenseFiles) { Copy-Item -LiteralPath $taskLicenseFile.FullName -Destination $taskNotices }
    }
    $taskCommit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    $taskInfo = "TMS Scoreboard Studio $taskVersion`r`nSource commit: $taskCommit`r`nPlatform: Windows x64`r`n.NET: bundled`r`nNDI runtime: not bundled`r`nMedia files: supplied by operator`r`n"
    [IO.File]::WriteAllText((Join-Path $taskPackage 'BUILD-INFO.txt'), $taskInfo)
    $taskZip = Join-Path $taskAssets ($taskName + '.zip')
    Compress-Archive -LiteralPath $taskPackage -DestinationPath $taskZip -CompressionLevel Optimal
    $taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskChecksums = $taskHash + '  ' + $taskName + ".zip`n"
    if ($UserGuide) {
        $taskGuideAsset = Join-Path $taskAssets 'TMS-Scoreboard-Studio-User-Guide.pdf'
        Copy-Item -LiteralPath $UserGuide -Destination $taskGuideAsset
        $taskGuideHash = (Get-FileHash -LiteralPath $taskGuideAsset -Algorithm SHA256).Hash.ToLowerInvariant()
        $taskChecksums += $taskGuideHash + "  TMS-Scoreboard-Studio-User-Guide.pdf`n"
    }
    [IO.File]::WriteAllText((Join-Path $taskAssets 'SHA256SUMS.txt'), $taskChecksums)
    Write-Output "Release assets: $taskAssets"
    Write-Output "ZIP: $taskZip"
} finally { Pop-Location }
