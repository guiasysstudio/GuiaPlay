[CmdletBinding()]
param(
    [string]$Channel,
    [string[]]$RuntimeIdentifiers = @('win-x64', 'win-x86')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'Read')
    $reader = $null
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Arquivo não possui cabeçalho MZ válido: $Path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) { throw "Offset PE inválido: $Path" }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Arquivo não possui assinatura PE válida: $Path" }
        return $reader.ReadUInt16()
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() } else { $stream.Dispose() }
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$requiredBrandingAssets = @(
    'src\GuiaPlay.App\Assets\Branding\Source\GuiaPlay-Logo.svg',
    'src\GuiaPlay.App\Assets\Branding\Source\GuiaPlay-Wordmark.svg',
    'src\GuiaPlay.App\Assets\Branding\Icons\GuiaPlay.ico',
    'src\GuiaPlay.App\Assets\Branding\GuiaPlay-Wordmark-UI.png',
    'src\GuiaPlay.App\Assets\Branding\Wordmarks\GuiaPlay-Wordmark-1024.png',
    'installer\Assets\GuiaPlay-Setup.ico',
    'installer\Assets\WizardImageFile.bmp',
    'installer\Assets\WizardSmallImageFile.bmp',
    'installer\Assets\GuiaPlay-Banner-700x200.png'
)
foreach ($relativePath in $requiredBrandingAssets) {
    $assetPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $assetPath) -or (Get-Item -LiteralPath $assetPath).Length -eq 0) {
        throw "Asset obrigatório de branding ausente ou vazio: $relativePath"
    }
}

$runtimeIdentifiers = @($RuntimeIdentifiers | Select-Object -Unique)
if ('win-x64' -notin $runtimeIdentifiers) {
    throw 'win-x64 é obrigatório porque package precisa permanecer compatível com leitores antigos.'
}
foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    if ($runtimeIdentifier -notin @('win-x64', 'win-x86')) {
        throw "RuntimeIdentifier não suportado: $runtimeIdentifier"
    }
}

[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$prefix = [string]$buildProps.Project.PropertyGroup.VersionPrefix
$suffix = [string]$buildProps.Project.PropertyGroup.VersionSuffix
$releaseDate = [string]$buildProps.Project.PropertyGroup.ProductReleaseDate
$configuredChannel = [string]$buildProps.Project.PropertyGroup.UpdateChannel
$version = if ([string]::IsNullOrWhiteSpace($suffix)) { $prefix } else { "$prefix-$suffix" }
$versionChannel = if ([string]::IsNullOrWhiteSpace($suffix)) {
    'Stable'
}
elseif ($suffix -match '^prototipo(?:[.-].*)?$') {
    'Prototype'
}
elseif ($suffix -match '^rc(?:\.?[0-9]+)(?:[.-].*)?$') {
    'ReleaseCandidate'
}
else {
    throw "O sufixo '$suffix' não pertence a um canal de release conhecido."
}
if ([string]::IsNullOrWhiteSpace($Channel)) {
    $Channel = $configuredChannel
}
if ($Channel -notin @('Prototype', 'ReleaseCandidate', 'Stable')) {
    throw "Canal inválido: $Channel"
}
if ($Channel -ne $versionChannel) {
    throw "A versão $version pertence ao canal $versionChannel, não ao canal $Channel."
}
$manifestChannel = switch ($Channel) {
    'Prototype' { 'prototype' }
    'ReleaseCandidate' { 'releaseCandidate' }
    'Stable' { 'stable' }
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not $artifactsRoot.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Caminho de artifacts fora do repositório.'
}
if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

$publishRoot = Join-Path $artifactsRoot 'publish'
$releaseDir = Join-Path $artifactsRoot 'release'
New-Item -ItemType Directory -Force -Path $publishRoot, $releaseDir | Out-Null
$manifestPath = Join-Path $releaseDir 'update-manifest.json'
$checksumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
$numericVersion = "$prefix.0"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

Push-Location $repoRoot
try {
    dotnet restore GuiaPlay.slnx
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
    dotnet format GuiaPlay.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet format falhou.' }
    dotnet build GuiaPlay.slnx -c Debug --no-restore "-p:UpdateChannel=$Channel"
    if ($LASTEXITCODE -ne 0) { throw 'Build Debug falhou.' }
    dotnet test GuiaPlay.slnx -c Debug --no-build --no-restore --logger 'console;verbosity=minimal' "-p:UpdateChannel=$Channel"
    if ($LASTEXITCODE -ne 0) { throw 'Testes Debug falharam.' }
    dotnet build GuiaPlay.slnx -c Release --no-restore "-p:UpdateChannel=$Channel"
    if ($LASTEXITCODE -ne 0) { throw 'Build Release falhou.' }
    dotnet test GuiaPlay.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal' "-p:UpdateChannel=$Channel"
    if ($LASTEXITCODE -ne 0) { throw 'Testes Release falharam.' }

    $packages = [ordered]@{}
    $artifactPaths = New-Object System.Collections.Generic.List[string]
    foreach ($runtimeIdentifier in $runtimeIdentifiers) {
        $architecture = if ($runtimeIdentifier -eq 'win-x64') { 'x64' } else { 'x86' }
        $publishDir = Join-Path $publishRoot "GuiaPlay-$runtimeIdentifier"
        $updaterDir = Join-Path $publishRoot "Updater-$runtimeIdentifier"
        New-Item -ItemType Directory -Force -Path $publishDir, $updaterDir | Out-Null

        dotnet restore src/GuiaPlay.App/GuiaPlay.App.csproj -r $runtimeIdentifier "-p:Platform=$architecture" "-p:PlatformTarget=$architecture" "-p:UpdateChannel=$Channel"
        if ($LASTEXITCODE -ne 0) { throw "Restore do GuiaPlay para $runtimeIdentifier falhou." }
        dotnet restore src/GuiaPlay.Updater/GuiaPlay.Updater.csproj -r $runtimeIdentifier "-p:Platform=$architecture" "-p:PlatformTarget=$architecture" "-p:UpdateChannel=$Channel"
        if ($LASTEXITCODE -ne 0) { throw "Restore do updater para $runtimeIdentifier falhou." }
        dotnet publish src/GuiaPlay.App/GuiaPlay.App.csproj -c Release -r $runtimeIdentifier --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Platform=$architecture" "-p:PlatformTarget=$architecture" "-p:UpdateChannel=$Channel" -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "Publish do GuiaPlay para $runtimeIdentifier falhou." }
        dotnet publish src/GuiaPlay.Updater/GuiaPlay.Updater.csproj -c Release -r $runtimeIdentifier --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false "-p:Platform=$architecture" "-p:PlatformTarget=$architecture" "-p:UpdateChannel=$Channel" -o $updaterDir
        if ($LASTEXITCODE -ne 0) { throw "Publish do updater para $runtimeIdentifier falhou." }
        Copy-Item -LiteralPath (Join-Path $updaterDir 'GuiaPlay.Updater.exe') -Destination (Join-Path $publishDir 'GuiaPlay.Updater.exe') -Force

        $marker = [ordered]@{ schema = 1; appId = 'GuiaSys.GuiaPlay'; version = $version; rid = $runtimeIdentifier } | ConvertTo-Json
        [System.IO.File]::WriteAllText((Join-Path $publishDir 'install.json'), $marker, $utf8NoBom)
        foreach ($requiredFile in @('GuiaPlay.exe', 'GuiaPlay.Updater.exe', "libvlc\$runtimeIdentifier\libvlc.dll", "libvlc\$runtimeIdentifier\plugins")) {
            if (-not (Test-Path -LiteralPath (Join-Path $publishDir $requiredFile))) {
                throw "Arquivo obrigatório ausente no publish $runtimeIdentifier`: $requiredFile"
            }
        }
        $oppositeRid = if ($runtimeIdentifier -eq 'win-x64') { 'win-x86' } else { 'win-x64' }
        if (Test-Path -LiteralPath (Join-Path $publishDir "libvlc\$oppositeRid")) {
            throw "Publish $runtimeIdentifier contém binários nativos de $oppositeRid."
        }
        $expectedMachine = if ($runtimeIdentifier -eq 'win-x64') { 0x8664 } else { 0x014C }
        foreach ($nativeFile in @(
            'GuiaPlay.exe',
            'GuiaPlay.Updater.exe',
            "libvlc\$runtimeIdentifier\libvlc.dll",
            "libvlc\$runtimeIdentifier\libvlccore.dll")) {
            $nativePath = Join-Path $publishDir $nativeFile
            $actualMachine = Get-PeMachine -Path $nativePath
            if ($actualMachine -ne $expectedMachine) {
                throw ("Arquitetura PE incorreta em {0}: esperado 0x{1:x4}, encontrado 0x{2:x4}." -f $nativeFile, $expectedMachine, $actualMachine)
            }
        }

        $zipName = "GuiaPlay-$version-$runtimeIdentifier.zip"
        $zipPath = Join-Path $releaseDir $zipName
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $packages[$runtimeIdentifier] = [ordered]@{ assetName = $zipName; sha256 = $zipHash }
        $artifactPaths.Add($zipPath)
    }

    $publishedAt = ([DateTimeOffset]::ParseExact($releaseDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)).ToString('yyyy-MM-ddT12:00:00Z')
    $manifest = [ordered]@{
        schema = 1
        version = $version
        channel = $manifestChannel
        publishedAt = $publishedAt
        package = $packages['win-x64']
        packages = $packages
    }
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $utf8NoBom)

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

    foreach ($runtimeIdentifier in $runtimeIdentifiers) {
        $publishDir = Join-Path $publishRoot "GuiaPlay-$runtimeIdentifier"
        $setupName = if ($runtimeIdentifier -eq 'win-x64') { "GuiaPlay-Setup-$version.exe" } else { "GuiaPlay-Setup-$version-win-x86.exe" }
        $setupBaseName = [System.IO.Path]::GetFileNameWithoutExtension($setupName)
        $allowedArchitectures = if ($runtimeIdentifier -eq 'win-x64') { 'x64compatible' } else { 'x86compatible' }
        $isccArguments = @(
            "/DMyAppVersion=$version",
            "/DNumericVersion=$numericVersion",
            "/DSourceDir=$publishDir",
            "/DReleaseDir=$releaseDir",
            "/DTargetRid=$runtimeIdentifier",
            "/DAllowedArchitectures=$allowedArchitectures",
            "/DSetupBaseName=$setupBaseName"
        )
        if ($runtimeIdentifier -eq 'win-x64') {
            $isccArguments += '/DInstallIn64BitMode=x64compatible'
        }
        & $iscc @isccArguments (Join-Path $repoRoot 'installer\GuiaPlay.iss')
        if ($LASTEXITCODE -ne 0) { throw "Compilação do instalador $runtimeIdentifier falhou." }
        $setupPath = Join-Path $releaseDir $setupName
        if (-not (Test-Path -LiteralPath $setupPath)) { throw "Instalador ausente: $setupName" }
        $artifactPaths.Add($setupPath)
    }

    $checksumLines = foreach ($assetPath in $artifactPaths) {
        $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($assetPath))"
    }
    $checksumLines | Set-Content -LiteralPath $checksumsPath -Encoding ascii
    $artifactPaths.Add($checksumsPath)
    $artifactPaths.Add($manifestPath)
    foreach ($asset in $artifactPaths) {
        if (-not (Test-Path -LiteralPath $asset) -or (Get-Item -LiteralPath $asset).Length -eq 0) {
            throw "Asset inválido: $asset"
        }
    }

    $manifestCheck = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifestCheck.package.assetName -ne $manifestCheck.packages.'win-x64'.assetName -or
        $manifestCheck.package.sha256 -ne $manifestCheck.packages.'win-x64'.sha256) {
        throw 'package deixou de ser o alias compatível do pacote win-x64.'
    }
    foreach ($runtimeIdentifier in $runtimeIdentifiers) {
        $package = $manifestCheck.packages.$runtimeIdentifier
        $packagePath = Join-Path $releaseDir $package.assetName
        $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $package.sha256) { throw "SHA divergente no manifesto para $runtimeIdentifier." }
    }

    Write-Host "Release $version ($Channel) montada com sucesso em $releaseDir"
    foreach ($asset in $artifactPaths) {
        Write-Host ([System.IO.Path]::GetFileName($asset))
    }
}
finally {
    Pop-Location
}
