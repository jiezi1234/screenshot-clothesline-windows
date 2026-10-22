param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet build Clothesline.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
} finally { Pop-Location }
