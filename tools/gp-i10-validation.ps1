#Requires -Version 5.1
<#
.SYNOPSIS
  GP-I10 — validation only, never creates installers, tags or Releases.
.DESCRIPTION
  Restore/build/test Debug and Release on Windows, then publish smoke folders
  for win-x64 and win-x86. Does not execute, install or deploy GuiaPlay.
  The operator must perform the physical multi-monitor/media tests manually.
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$SkipReleaseTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'GuiaPlay.slnx'
$app = Join-Path $root 'src/GuiaPlay.App/GuiaPlay.App.csproj'
$target = Join-Path $root 'artifacts/gp-i10-validation'
New-Item -Path $target -ItemType Directory -Force | Out-Null
Set-Location $root

$transcript = Join-Path $target 'validation-transcript.txt'
try {
    Start-Transcript -Path $transcript -Force | Out-Null
} catch {
    Write-Warning "Não foi possível abrir o transcript. Saída permanece no terminal."
}

function Run-Checked {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string[]]$Parameters
    )
    Write-Host ""
    Write-Host "=== $Label ===" -ForegroundColor Cyan
    & dotnet @Parameters
    if ($LASTEXITCODE -ne 0) {
        throw "$Label falhou com exit code $LASTEXITCODE. Nenhuma publicação será feita."
    }
}

try {
    $sdk = Get-Command dotnet -ErrorAction Stop
    Write-Host "Validação GUIAPLAY GP-I10: $($sdk.Source)"
    Run-Checked 'Identificar SDK .NET' @('--info')
    Run-Checked 'Restaurar solução' @('restore', $solution)
    Run-Checked 'Compilar Debug' @('build', $solution, '--configuration', 'Debug', '--no-restore')
    Run-Checked 'Testes Debug' @('test', $solution, '--configuration', 'Debug', '--no-build', '--no-restore')

    Run-Checked 'Compilar Release' @('build', $solution, '--configuration', 'Release', '--no-restore')
    if (-not $SkipReleaseTests) {
        Run-Checked 'Testes Release' @('test', $solution, '--configuration', 'Release', '--no-build', '--no-restore')
    }

    if (-not $SkipPublish) {
        foreach ($runtime in @('win-x64', 'win-x86')) {
            $output = Join-Path $target $runtime
            # Directory.Build.props defaults both RuntimeIdentifier and PlatformTarget
            # to x64. Explicitly override BOTH to avoid labeling an x64 executable x86.
            $architecture = if ($runtime -eq 'win-x86') { 'x86' } else { 'x64' }
            Run-Checked "Restaurar pacote $runtime" @(
                'restore', $app, '--runtime', $runtime,
                "-p:PlatformTarget=$architecture")
            Run-Checked "Publicar pasta de testes $runtime" @(
                'publish', $app, '--configuration', 'Release', '--runtime', $runtime,
                '--self-contained', 'false', '--no-restore',
                '--output', $output,
                "-p:PlatformTarget=$architecture",
                '-p:PublishSingleFile=false')
            if (-not (Test-Path (Join-Path $output 'GuiaPlay.exe'))) {
                throw "Binário GuiaPlay.exe não encontrado no pacote $runtime."
            }
            if (-not (Test-Path (Join-Path $output 'Assets/YouTube/player.html'))) {
                throw "Player WebView2 não incluído no output de $runtime."
            }

            Write-Host "Pasta de smoke pronta: $output" -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "ETAPA CLI CONCLUÍDA — pendente smoke humano de UI, telas e reprodução." -ForegroundColor Green
    Write-Host "Esta operação NÃO instalou nem executou o programa."
} finally {
    try {
        Stop-Transcript | Out-Null
    } catch {
        # A failed/missing transcript does not invalidate the CLI exit code.
    }
}
