[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not $artifactsRoot.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Caminho de artifacts fora do repositório.'
}

if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

$publishDir = Join-Path $artifactsRoot 'publish\GuiaPlay'
$updaterDir = Join-Path $artifactsRoot 'publish\Updater'
$releaseDir = Join-Path $artifactsRoot 'release'
$installerWorkDir = Join-Path $artifactsRoot 'installer'
New-Item -ItemType Directory -Force -Path $publishDir, $updaterDir, $releaseDir, $installerWorkDir | Out-Null

[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$prefix = [string]$buildProps.Project.PropertyGroup.VersionPrefix
$suffix = [string]$buildProps.Project.PropertyGroup.VersionSuffix
$releaseDate = [string]$buildProps.Project.PropertyGroup.ProductReleaseDate
$version = if ([string]::IsNullOrWhiteSpace($suffix)) { $prefix } else { "$prefix-$suffix" }
$numericVersion = "$prefix.0"
$zipName = "GuiaPlay-$version-win-x64.zip"
$setupName = "GuiaPlay-Setup-$version.exe"
$zipPath = Join-Path $releaseDir $zipName
$setupPath = Join-Path $releaseDir $setupName
$manifestPath = Join-Path $releaseDir 'update-manifest.json'
$checksumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
$markerPath = Join-Path $installerWorkDir 'install.json'

Push-Location $repoRoot
try {
    dotnet restore GuiaPlay.slnx
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
    dotnet format GuiaPlay.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet format falhou.' }
    dotnet build GuiaPlay.slnx -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build Debug falhou.' }
    dotnet build GuiaPlay.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build Release falhou.' }
    dotnet test GuiaPlay.slnx -c Debug --no-build --no-restore --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Testes Debug falharam.' }
    dotnet test GuiaPlay.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Testes Release falharam.' }

    dotnet publish src/GuiaPlay.App/GuiaPlay.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish do GuiaPlay falhou.' }
    dotnet publish src/GuiaPlay.Updater/GuiaPlay.Updater.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o $updaterDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish do updater falhou.' }
    Copy-Item -LiteralPath (Join-Path $updaterDir 'GuiaPlay.Updater.exe') -Destination (Join-Path $publishDir 'GuiaPlay.Updater.exe') -Force

    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'GuiaPlay.exe'))) { throw 'GuiaPlay.exe ausente no publish.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'libvlc\win-x64\libvlc.dll'))) { throw 'LibVLC nativo ausente no publish.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'GuiaPlay.Updater.exe'))) { throw 'Updater ausente no publish.' }

    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $publishedAt = ([DateTimeOffset]::ParseExact($releaseDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)).ToString('yyyy-MM-ddT12:00:00Z')
    $manifest = [ordered]@{
        schema = 1
        version = $version
        channel = 'prototype'
        publishedAt = $publishedAt
        package = [ordered]@{ assetName = $zipName; sha256 = $zipHash }
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8NoBom)
    $marker = [ordered]@{ schema = 1; appId = 'GuiaSys.GuiaPlay'; version = $version } | ConvertTo-Json
    [System.IO.File]::WriteAllText($markerPath, $marker, $utf8NoBom)

    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
    if (-not $iscc) {
        $candidates = @(
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe',
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        )
        $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) não foi encontrado.' }

    & $iscc "/DMyAppVersion=$version" "/DNumericVersion=$numericVersion" "/DSourceDir=$publishDir" "/DMarkerPath=$markerPath" "/DReleaseDir=$releaseDir" (Join-Path $repoRoot 'installer\GuiaPlay.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Compilação do instalador falhou.' }
    if (-not (Test-Path -LiteralPath $setupPath)) { throw "Instalador ausente: $setupName" }

    $setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    @(
        "$zipHash  $zipName"
        "$setupHash  $setupName"
    ) | Set-Content -LiteralPath $checksumsPath -Encoding ascii

    foreach ($asset in @($zipPath, $setupPath, $checksumsPath, $manifestPath)) {
        if (-not (Test-Path -LiteralPath $asset) -or (Get-Item -LiteralPath $asset).Length -eq 0) {
            throw "Asset inválido: $asset"
        }
    }

    $manifestCheck = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifestCheck.package.sha256 -ne $zipHash -or $manifestCheck.package.assetName -ne $zipName) {
        throw 'Validação do update-manifest falhou.'
    }

    Write-Host "Release $version montada com sucesso em $releaseDir"
    Write-Host "ZIP SHA-256: $zipHash"
    Write-Host "Setup SHA-256: $setupHash"
}
finally {
    Pop-Location
}
