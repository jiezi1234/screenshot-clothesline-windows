param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet publish src/Clothesline.App --configuration Release --runtime win-x64 --self-contained true -p:PublishTrimmed=false --output dist/Clothesline-0.12.0-win-x64
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    $bundle = 'dist/Clothesline-0.12.0-win-x64'
    $assets = Get-Content src/Clothesline.App/obj/project.assets.json -Raw | ConvertFrom-Json
    $packages = @($assets.packageFolders.PSObject.Properties.Name)[0]
    $configuration = Get-Content "$bundle/Clothesline.runtimeconfig.json" -Raw | ConvertFrom-Json
    $licenses = Join-Path $bundle 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    foreach ($framework in $configuration.runtimeOptions.includedFrameworks) {
        $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $package = Join-Path $packages "$id/$($framework.version)"
        $notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
        if ($notices.Count -eq 0) { throw "Missing runtime license in $package" }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses "$id-$($framework.version)-$($notice.Name)") }
    }
    Copy-Item licenses/* -Destination $licenses -Force
    # Windows SDK projection is an implicit framework/runtime pack, absent from assets.libraries.
    $dependencies = Get-Content "$bundle/Clothesline.deps.json" -Raw | ConvertFrom-Json
    foreach ($pack in $dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.Windows.SDK.NET.Ref/*' }) {
        $id, $version = $pack.Replace('runtimepack.', '').Split('/')
        $spec = Join-Path $packages "$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).nuspec"
        Copy-Item -LiteralPath $spec -Destination (Join-Path $licenses "$id-$version.nuspec")
    }
    foreach ($dependency in $assets.libraries.PSObject.Properties) {
        if ($dependency.Value.type -ne 'package') { continue }
        $id, $version = $dependency.Name.Split('/')
        $spec = Join-Path $packages "$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).nuspec"
        Copy-Item -LiteralPath $spec -Destination (Join-Path $licenses "$id-$version.nuspec")
    }
    Copy-Item README.md, THIRD_PARTY_NOTICES.md, CONTRIBUTING.md -Destination $bundle
    Copy-Item docs -Destination $bundle -Recurse -Force
    Compress-Archive -Path $bundle -DestinationPath "$bundle.zip" -Force
    $hash = Get-FileHash "$bundle.zip" -Algorithm SHA256
    "$($hash.Hash)  Clothesline-0.12.0-win-x64.zip" | Set-Content dist/SHA256SUMS-0.12.0.txt
    $hash | Format-List
} finally { Pop-Location }
