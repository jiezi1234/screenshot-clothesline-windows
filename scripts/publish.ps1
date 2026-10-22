param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet publish src/Clothesline.App --configuration Release --runtime win-x64 --self-contained true -p:PublishTrimmed=false --output dist/Clothesline-Prototype-win-x64
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    $bundle = 'dist/Clothesline-Prototype-win-x64'
    $assets = Get-Content src/Clothesline.App/obj/project.assets.json -Raw | ConvertFrom-Json
    $packages = @($assets.packageFolders.PSObject.Properties.Name)[0]
    $configuration = Get-Content "$bundle/Clothesline.Prototype.runtimeconfig.json" -Raw | ConvertFrom-Json
    $licenses = Join-Path $bundle 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    foreach ($framework in $configuration.runtimeOptions.includedFrameworks) {
        $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $package = Join-Path $packages "$id/$($framework.version)"
        $notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
        if ($notices.Count -eq 0) { throw "Missing runtime license in $package" }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses "$id-$($framework.version)-$($notice.Name)") }
    }
    Copy-Item README.md, THIRD_PARTY_NOTICES.md, CONTRIBUTING.md -Destination $bundle
    Copy-Item docs -Destination $bundle -Recurse -Force
    Compress-Archive -Path dist/Clothesline-Prototype-win-x64 -DestinationPath dist/Clothesline-Prototype-win-x64.zip -Force
    Get-FileHash dist/Clothesline-Prototype-win-x64.zip -Algorithm SHA256 | Format-List
} finally { Pop-Location }
