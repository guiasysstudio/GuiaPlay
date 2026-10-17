#Requires -Version 5.1
<#
.SYNOPSIS
  GP-I10 - local Windows validation. Never publishes a GitHub Release.
.DESCRIPTION
  Compatible with Windows PowerShell 5.1 (ASCII source; no UTF-8 BOM needed).
  Restores/builds/tests Debug and Release and creates local smoke folders.
  The optional -BuildLocalInstallers switch creates ISOLATED TEST installers.
  No tags, pushes, uploads, GitHub Releases or production deployments.
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$SkipReleaseTests,
    [switch]$BuildLocalInstallers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $root 'GuiaPlay.slnx'
$app = Join-Path $root 'src/GuiaPlay.App/GuiaPlay.App.csproj'
$target = Join-Path $root 'artifacts/gp-i10-validation'
New-Item -Path $target -ItemType Directory -Force | Out-Null

if ($BuildLocalInstallers -and $SkipPublish) {
    throw '-BuildLocalInstallers cannot be combined with -SkipPublish.'
}
if ($BuildLocalInstallers -and $SkipReleaseTests) {
    throw 'Local test installers require the full Debug and Release validation.'
}

function Invoke-CheckedDotnet {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string[]]$Arguments
    )
    Write-Host ''
    Write-Host ('=== ' + $Label + ' ===') -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed (exit code {1}). No installer was published." -f $Label, $LASTEXITCODE)
    }
}

$transcript = Join-Path $target 'validation-transcript.txt'
$transcriptStarted = $false
Push-Location $root
try {
    try {
        Start-Transcript -Path $transcript -Force -ErrorAction Stop | Out-Null
        $transcriptStarted = $true
    } catch {
        Write-Warning 'Unable to open transcript. Terminal output is still available.'
    }

    $sdk = Get-Command dotnet -ErrorAction Stop
    Write-Host ('GP-I10 Windows validation - SDK: ' + $sdk.Source)
    Invoke-CheckedDotnet 'Identify .NET SDK' @('--info')
    Invoke-CheckedDotnet 'Restore solution' @('restore', $solution)
    Invoke-CheckedDotnet 'Build Debug' @('build', $solution, '-c', 'Debug', '--no-restore')
    Invoke-CheckedDotnet 'Test Debug' @('test', $solution, '-c', 'Debug', '--no-build', '--no-restore')
    Invoke-CheckedDotnet 'Build Release' @('build', $solution, '-c', 'Release', '--no-restore')
    if (-not $SkipReleaseTests) {
        Invoke-CheckedDotnet 'Test Release' @('test', $solution, '-c', 'Release', '--no-build', '--no-restore')
    }

    if (-not $SkipPublish) {
        foreach ($runtime in @('win-x64', 'win-x86')) {
            $output = Join-Path $target $runtime
            $architecture = if ($runtime -eq 'win-x86') { 'x86' } else { 'x64' }
            Invoke-CheckedDotnet ("Restore smoke " + $runtime) @(
                'restore', $app, '-r', $runtime,
                "-p:PlatformTarget=$architecture")
            Invoke-CheckedDotnet ("Publish smoke " + $runtime) @(
                'publish', $app, '-c', 'Release', '-r', $runtime,
                '--self-contained', 'false', '--no-restore',
                '-p:PublishSingleFile=false',
                "-p:PlatformTarget=$architecture",
                '-o', $output)

            foreach ($required in @('GuiaPlay.exe', 'Assets/YouTube/player.html')) {
                if (-not (Test-Path -LiteralPath (Join-Path $output $required))) {
                    throw ("Missing smoke file for {0}: {1}" -f $runtime, $required)
                }
            }
            Write-Host ('Local smoke folder: ' + $output) -ForegroundColor Green
        }
    }

    if ($BuildLocalInstallers) {
        $installerScript = Join-Path $root 'tools/gp-i10-installers-local.ps1'
        if (-not (Test-Path -LiteralPath $installerScript)) {
            throw 'Local installer script is missing.'
        }
        Write-Host ''
        Write-Host '=== Build isolated LOCAL TEST installers (no upload) ===' -ForegroundColor Cyan
        & $installerScript
        if (-not $?) {
            throw 'Local test installer generation failed.'
        }
    }

    Write-Host ''
    Write-Host 'GP-I10 CLI VALIDATION COMPLETED.' -ForegroundColor Green
    Write-Host 'Physical UI, monitors, audio and YouTube tests are still required.'
    Write-Host 'No tag, GitHub Release, production upload or deployment was performed.'
}
finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch { }
    }
    Pop-Location
}
