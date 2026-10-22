param([string]$Dotnet = 'dotnet', [switch]$Desktop)
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
        & $executable src/Clothesline.App/bin/Release/net10.0-windows/Clothesline.dll --self-test artifacts/validation
        if ($LASTEXITCODE -ne 0) { throw "Desktop checks failed. See artifacts/validation/diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows/Clothesline.dll --library-test artifacts/stage1-validation
        if ($LASTEXITCODE -ne 0) { throw "Library checks failed. See artifacts/stage1-validation/stage1-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows/Clothesline.dll --editor-test artifacts/stage2-validation
        if ($LASTEXITCODE -ne 0) { throw "Editor checks failed. See artifacts/stage2-validation/editor-diagnostics.json" }
        & $executable src/Clothesline.App/bin/Release/net10.0-windows/Clothesline.dll --preferences-test artifacts/preferences-validation
        if ($LASTEXITCODE -ne 0) { throw "Preferences checks failed. See artifacts/preferences-validation/preferences-diagnostics.json" }
    }
} finally { $env:DOTNET_ROOT = $previousRoot; Pop-Location }
