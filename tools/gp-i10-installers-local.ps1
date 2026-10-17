#Requires -Version 5.1
<#
.SYNOPSIS
  Build LOCAL TEST x64 and x86 GuiaPlay Interface 3 installers using Inno Setup.
.DESCRIPTION
  Does NOT modify the stable installation, does NOT publish anything and does
  NOT invoke scripts/build-release.ps1 or scripts/publish-release.ps1.
  Uses distinct Inno AppIds and install directories for both architectures.
  Windows PowerShell 5.1 compatible (ASCII source).
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $root 'src/GuiaPlay.App/GuiaPlay.App.csproj'
$iss = Join-Path $root 'installer/GuiaPlay-Interface3-Test.iss'
$staging = Join-Path $root 'artifacts/gp-i10-local-installer-staging'
$installers = Join-Path $root 'artifacts/gp-i10-local-installers'
$sourceRoot = Join-Path $root 'src/GuiaPlay.App'

function Invoke-DotnetChecked {
    param([string]$Name, [string[]]$Arguments)
    Write-Host ('=== ' + $Name + ' ===') -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed (exit code {1})." -f $Name, $LASTEXITCODE)
    }
}

function Get-PEMachine {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    $reader = $null
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw ('Missing MZ header: ' + $Path) }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -le 0 -or $offset + 6 -ge $stream.Length) {
            throw ('Invalid PE offset: ' + $Path)
        }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw ('Missing PE header: ' + $Path) }
        return $reader.ReadUInt16()
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        else { $stream.Dispose() }
    }
}

$innoCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$inno = if ($null -ne $innoCommand) { $innoCommand.Source } else { $null }
if (-not $inno) {
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $inno = $candidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}
if (-not $inno) {
    throw 'Inno Setup 6 (ISCC.exe) not found. Install Inno Setup 6 before generating local test installers.'
}
if (-not (Test-Path -LiteralPath $iss)) {
    throw ('Local test Inno script not found: ' + $iss)
}

[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$prefix = [string]$props.Project.PropertyGroup.VersionPrefix
if ($prefix -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Invalid VersionPrefix; expected numeric major.minor.patch.'
}
$previewSuffix = 'prototipo.gp-i10'
$previewVersion = $prefix + '-' + $previewSuffix
$numericVersion = $prefix + '.0'

# Warn about the shared user profile and protect it before the testers run
# either installed version. The test installer itself changes no settings.
$userData = Join-Path $env:LOCALAPPDATA 'GuiaSys\GuiaPlay'
if (Test-Path -LiteralPath $userData) {
    $backupBase = Join-Path $env:LOCALAPPDATA 'GuiaSys\GuiaPlay-Interface3-Backups'
    New-Item -Path $backupBase -ItemType Directory -Force | Out-Null
    $backup = Join-Path $backupBase (
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    Copy-Item -LiteralPath $userData -Destination $backup -Recurse -Force -ErrorAction Stop
    Write-Host ('Existing profile backup (private, outside Git): ' + $backup) -ForegroundColor Yellow
}

New-Item -Path $staging, $installers -ItemType Directory -Force | Out-Null
$results = New-Object System.Collections.Generic.List[string]

Push-Location $root
try {
    foreach ($runtime in @('win-x64', 'win-x86')) {
        $arch = if ($runtime -eq 'win-x64') { 'x64' } else { 'x86' }
        $machine = if ($arch -eq 'x64') { 0x8664 } else { 0x014c }
        $sourceDir = Join-Path $staging $runtime
        New-Item -Path $sourceDir -ItemType Directory -Force | Out-Null

        $buildArgs = @(
            "-p:PlatformTarget=$arch",
            "-p:VersionSuffix=$previewSuffix",
            '-p:UpdateChannel=Prototype'
        )
        Invoke-DotnetChecked ('Restore ' + $runtime) (
            @('restore', $app, '-r', $runtime) + $buildArgs)
        Invoke-DotnetChecked ('Self-contained publish ' + $runtime) (
            @('publish', $app, '-c', 'Release', '-r', $runtime,
              '--self-contained', 'true', '--no-restore',
              '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
              '-o', $sourceDir) + $buildArgs)

        foreach ($required in @(
            'GuiaPlay.exe',
            'Assets\YouTube\player.html',
            "libvlc\$runtime\libvlc.dll",
            "libvlc\$runtime\plugins"
        )) {
            if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $required))) {
                throw ("Missing file in {0}: {1}" -f $runtime, $required)
            }
        }
        foreach ($file in @('GuiaPlay.exe', "libvlc\$runtime\libvlc.dll")) {
            $nativePath = Join-Path $sourceDir $file
            $foundMachine = Get-PEMachine -Path $nativePath
            if ($foundMachine -ne $machine) {
                throw ("Architecture mismatch: {0} expected 0x{1:x4}, got 0x{2:x4}" -f
                    $file, $machine, $foundMachine)
            }
        }

        # An installer for this test must never be mistaken for a production update.
        if (Test-Path -LiteralPath (Join-Path $sourceDir 'install.json')) {
            throw 'Production install.json marker is forbidden in a local test installer.'
        }

        $testAppId = if ($arch -eq 'x64') {
            '{{2AB46ACB-161A-43C8-A22C-D5A99E6C4E24}'
        } else {
            '{{93B7DF53-215A-468A-9D49-5BF8405E81B9}'
        }
        $installDir = 'GuiaPlay-Interface3-TEST-' + $arch
        $setupBase = 'GuiaPlay-Interface3-TEST-' + $previewVersion + '-' + $runtime
        $allowedArch = if ($arch -eq 'x64') { 'x64compatible' } else { 'x86compatible' }
        $arguments = @(
            "/DMyAppVersion=$previewVersion",
            "/DNumericVersion=$numericVersion",
            "/DSourceDir=$sourceDir",
            "/DReleaseDir=$installers",
            "/DTargetRid=$runtime",
            "/DTestAppId=$testAppId",
            "/DTestInstallDir=$installDir",
            "/DAllowedArchitectures=$allowedArch",
            "/DSetupBaseName=$setupBase"
        )
        if ($arch -eq 'x64') {
            $arguments += '/DInstallIn64BitMode=x64compatible'
        }

        Write-Host ('=== Inno Setup local test ' + $runtime + ' ===') -ForegroundColor Cyan
        & $inno @arguments $iss
        if ($LASTEXITCODE -ne 0) {
            throw ('Local Inno compilation failed: ' + $runtime)
        }

        $setup = Join-Path $installers ($setupBase + '.exe')
        if (-not (Test-Path -LiteralPath $setup) -or
            (Get-Item -LiteralPath $setup).Length -le 0) {
            throw ('Expected installer not found: ' + $setup)
        }
        $results.Add($setup)
    }

    $checksums = Join-Path $installers 'SHA256SUMS.txt'
    $records = foreach ($setup in $results) {
        $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        $hash + '  ' + [System.IO.Path]::GetFileName($setup)
    }
    [System.IO.File]::WriteAllLines(
        $checksums, [string[]]$records, (New-Object System.Text.ASCIIEncoding))

    Write-Host ''
    Write-Host 'LOCAL TEST INSTALLERS CREATED. NO UPLOAD OR GITHUB RELEASE.' -ForegroundColor Green
    $results | ForEach-Object { Write-Host $_ }
    Write-Host ('SHA256: ' + $checksums)
    Write-Warning 'Do not run stable and test GuiaPlay simultaneously. Test versions still share the user profile and single-instance IPC; user profile was backed up when present.'
}
finally {
    Pop-Location
}
