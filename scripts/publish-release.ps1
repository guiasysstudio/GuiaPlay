[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$prefix = [string]$buildProps.Project.PropertyGroup.VersionPrefix
$suffix = [string]$buildProps.Project.PropertyGroup.VersionSuffix
$version = if ([string]::IsNullOrWhiteSpace($suffix)) { $prefix } else { "$prefix-$suffix" }
$tag = "v$version"
$releaseDir = Join-Path $repoRoot 'artifacts\release'
$notesPath = Join-Path $repoRoot "docs\releases\$version.md"
$expectedOrigin = 'https://github.com/guiasysstudio/GuiaPlay.git'
$assets = @(
    (Join-Path $releaseDir "GuiaPlay-$version-win-x64.zip"),
    (Join-Path $releaseDir "GuiaPlay-Setup-$version.exe"),
    (Join-Path $releaseDir 'SHA256SUMS.txt'),
    (Join-Path $releaseDir 'update-manifest.json')
)

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
    gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) { throw "A Release $tag já existe." }
    if (-not (Test-Path -LiteralPath $notesPath)) { throw 'Arquivo de release notes ausente.' }
    foreach ($asset in $assets) {
        if (-not (Test-Path -LiteralPath $asset) -or (Get-Item -LiteralPath $asset).Length -eq 0) { throw "Asset ausente ou vazio: $asset" }
    }

    $sums = Get-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt')
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
    gh release create $tag @assets --title "GuiaPlay $version" --notes-file $notesPath --prerelease --verify-tag
    if ($LASTEXITCODE -ne 0) { throw 'Criação da GitHub Release falhou.' }
    gh release view $tag --json url,tagName,isDraft,isPrerelease,publishedAt,assets
    if ($LASTEXITCODE -ne 0) { throw 'Verificação final da GitHub Release falhou.' }
}
finally {
    Pop-Location
}
