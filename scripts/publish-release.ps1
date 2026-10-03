[CmdletBinding()]
param(
    [string]$Channel
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$prefix = [string]$buildProps.Project.PropertyGroup.VersionPrefix
$suffix = [string]$buildProps.Project.PropertyGroup.VersionSuffix
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
if ($Channel -notin @('Prototype', 'ReleaseCandidate', 'Stable') -or $Channel -ne $versionChannel) {
    throw "Canal $Channel incompatível com a versão $version ($versionChannel)."
}
$expectedManifestChannel = switch ($Channel) {
    'Prototype' { 'prototype' }
    'ReleaseCandidate' { 'releaseCandidate' }
    'Stable' { 'stable' }
}
$isPrerelease = $Channel -ne 'Stable'

$tag = "v$version"
$releaseDir = Join-Path $repoRoot 'artifacts\release'
$notesPath = Join-Path $repoRoot "docs\releases\$version.md"
$manifestPath = Join-Path $releaseDir 'update-manifest.json'
$checksumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
$expectedOrigin = 'https://github.com/guiasysstudio/GuiaPlay.git'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'update-manifest.json ausente.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $version -or $manifest.channel -ne $expectedManifestChannel) {
    throw 'O manifesto não corresponde à versão/canal configurados.'
}
if ($manifest.package.assetName -ne $manifest.packages.'win-x64'.assetName -or
    $manifest.package.sha256 -ne $manifest.packages.'win-x64'.sha256) {
    throw 'O campo package não é o alias do pacote win-x64.'
}

$assets = New-Object System.Collections.Generic.List[string]
foreach ($property in $manifest.packages.PSObject.Properties) {
    if ($property.Name -notin @('win-x64', 'win-x86')) { throw "RID inesperado no manifesto: $($property.Name)" }
    $assets.Add((Join-Path $releaseDir ([string]$property.Value.assetName)))
    $setupName = if ($property.Name -eq 'win-x64') {
        "GuiaPlay-Setup-$version.exe"
    }
    else {
        "GuiaPlay-Setup-$version-win-x86.exe"
    }
    $assets.Add((Join-Path $releaseDir $setupName))
}
$assets.Add($checksumsPath)
$assets.Add($manifestPath)

Push-Location $repoRoot
try {
    if ((git branch --show-current) -ne 'main') { throw 'A branch atual não é main.' }
    if (git status --porcelain) { throw 'A working tree não está limpa.' }
    if ((git remote get-url origin) -ne $expectedOrigin) { throw 'O origin não é o repositório oficial esperado.' }
    gh auth status
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI não está autenticado.' }
    git fetch origin --tags --prune
    if ($LASTEXITCODE -ne 0) { throw 'git fetch falhou.' }
    $sync = (git rev-list --left-right --count main...origin/main) -split '\s+'
    if ($sync[0] -ne '0' -or $sync[1] -ne '0') { throw 'main local e origin/main não estão sincronizadas.' }
    if (git tag --list $tag) { throw "A tag local $tag já existe." }
    if (git ls-remote --exit-code --tags origin "refs/tags/$tag" 2>$null) { throw "A tag remota $tag já existe." }
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    gh release view $tag *> $null
    $releaseViewExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    if ($releaseViewExitCode -eq 0) { throw "A Release $tag já existe." }
    if (-not (Test-Path -LiteralPath $notesPath)) { throw 'Arquivo de release notes ausente.' }
    foreach ($asset in $assets) {
        if (-not (Test-Path -LiteralPath $asset) -or (Get-Item -LiteralPath $asset).Length -eq 0) {
            throw "Asset ausente ou vazio: $asset"
        }
    }

    $sums = Get-Content -LiteralPath $checksumsPath
    foreach ($line in $sums) {
        if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw "Linha de checksum inválida: $line" }
        $assetPath = Join-Path $releaseDir $Matches[2]
        $actual = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Matches[1]) { throw "Checksum divergente: $($Matches[2])" }
    }

    git tag -a $tag -m "GuiaPlay $version"
    if ($LASTEXITCODE -ne 0) { throw 'Criação da tag falhou.' }
    git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw 'Push da tag falhou; nenhuma Release foi criada.' }
    $releaseArguments = @(
        'release', 'create', $tag
    ) + @($assets) + @(
        '--title', "GuiaPlay $version",
        '--notes-file', $notesPath,
        '--verify-tag'
    )
    if ($isPrerelease) {
        $releaseArguments += '--prerelease'
    }
    & gh @releaseArguments
    if ($LASTEXITCODE -ne 0) { throw 'Criação da GitHub Release falhou.' }

    $verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("GuiaPlayReleaseVerify-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    try {
        gh release download $tag --dir $verificationRoot
        if ($LASTEXITCODE -ne 0) { throw 'Download de verificação dos assets publicados falhou.' }
        foreach ($asset in $assets) {
            $assetName = [System.IO.Path]::GetFileName($asset)
            $downloadedAsset = Join-Path $verificationRoot $assetName
            if (-not (Test-Path -LiteralPath $downloadedAsset)) {
                throw "Asset publicado não foi baixado novamente: $assetName"
            }

            $localHash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
            $downloadedHash = (Get-FileHash -LiteralPath $downloadedAsset -Algorithm SHA256).Hash
            if ($localHash -ne $downloadedHash) {
                throw "O asset publicado diverge byte a byte do arquivo local: $assetName"
            }
        }
    }
    finally {
        $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedVerificationRoot = [System.IO.Path]::GetFullPath($verificationRoot)
        if (-not $resolvedVerificationRoot.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'A pasta de verificação saiu da raiz temporária esperada.'
        }

        if (Test-Path -LiteralPath $resolvedVerificationRoot) {
            Remove-Item -LiteralPath $resolvedVerificationRoot -Recurse -Force
        }
    }

    gh release view $tag --json url,tagName,isDraft,isPrerelease,publishedAt,assets
    if ($LASTEXITCODE -ne 0) { throw 'Verificação final da GitHub Release falhou.' }
}
finally {
    Pop-Location
}
