param([string]$Dotnet = 'dotnet', [switch]$Desktop, [switch]$Soak)
$ErrorActionPreference = 'Stop'
$executable = (Get-Command $Dotnet -ErrorAction Stop).Source
Push-Location (Join-Path $PSScriptRoot '..')
$previousRoot = $env:DOTNET_ROOT
try {
    & $executable build Clothesline.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    & $executable run --project tests/Clothesline.Core.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Core checks failed: $LASTEXITCODE" }
    & $executable run --project tests/Clothesline.Storage.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Storage checks failed: $LASTEXITCODE" }
    if ($Desktop) {
        # Interactive desktop required. This starts only our own test windows.
        $env:DOTNET_ROOT = Split-Path $executable
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --drag-drop-test artifacts/drag-drop-validation
        if ($LASTEXITCODE -ne 0) { throw "Drag/drop checks failed. See artifacts/drag-drop-validation/drag-drop-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --dashboard-test artifacts/dashboard-validation
        if ($LASTEXITCODE -ne 0) { throw "Dashboard checks failed. See artifacts/dashboard-validation/dashboard-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --self-test artifacts/validation
        if ($LASTEXITCODE -ne 0) { throw "Desktop checks failed. See artifacts/validation/diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --library-test artifacts/stage1-validation
        if ($LASTEXITCODE -ne 0) { throw "Library checks failed. See artifacts/stage1-validation/stage1-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --editor-test artifacts/stage2-validation
        if ($LASTEXITCODE -ne 0) { throw "Editor checks failed. See artifacts/stage2-validation/editor-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --preferences-test artifacts/preferences-validation
        if ($LASTEXITCODE -ne 0) { throw "Preferences checks failed. See artifacts/preferences-validation/preferences-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --appearance-test artifacts/appearance-validation
        if ($LASTEXITCODE -ne 0) { throw "Appearance checks failed. See artifacts/appearance-validation/appearance-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --motion-test artifacts/motion-validation
        if ($LASTEXITCODE -ne 0) { throw "Motion checks failed. See artifacts/motion-validation/motion-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --video-test artifacts/video-validation
        if ($LASTEXITCODE -ne 0) { throw "Video checks failed. See artifacts/video-validation/video-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --image-format-test artifacts/image-format-validation
        if ($LASTEXITCODE -ne 0) { throw "Image format checks failed. See artifacts/image-format-validation/image-format-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --compatibility-test artifacts/compatibility-validation
        if ($LASTEXITCODE -ne 0) { throw "Compatibility checks failed. See artifacts/compatibility-validation/compatibility-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --performance-test artifacts/performance-validation
        if ($LASTEXITCODE -ne 0) { throw "Performance checks failed. See artifacts/performance-validation/performance-diagnostics.json" }
    }
    if ($Soak) {
        $env:DOTNET_ROOT = Split-Path $executable
        & $executable src/Clothesline.App/bin/Release/net10.0-windows10.0.19041.0/Clothesline.dll --soak-test artifacts/soak-validation
        if ($LASTEXITCODE -ne 0) { throw "Soak checks failed. See artifacts/soak-validation/soak-diagnostics.json" }
    }
} finally { $env:DOTNET_ROOT = $previousRoot; Pop-Location }
