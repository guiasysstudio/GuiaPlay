#Requires -Version 5.1
<#
.SYNOPSIS
  Run one controlled GP-I11 native-crash isolation matrix on Windows.
.DESCRIPTION
  Launches the Debug GuiaPlay executable with independent diagnostic flags.
  License/auth and personal settings remain unchanged. No installation,
  registry changes, uploads, tag or Release.
  Close the stable Guiaplay before running; this script rejects any existing
  GuiaPlay.exe processes rather than killing user programs.
#>
[CmdletBinding()]
param([int]$SecondsPerCase = 16)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($SecondsPerCase -lt 8 -or $SecondsPerCase -gt 60) {
    throw 'SecondsPerCase must be from 8 to 60.'
}
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = Join-Path $root 'src/GuiaPlay.App/bin/Debug/net10.0-windows/win-x64/GuiaPlay.exe'
$out = Join-Path $root 'artifacts/gp-i11-isolation'
New-Item -Path $out -ItemType Directory -Force | Out-Null
$results = New-Object 'System.Collections.Generic.List[string]'
$flags = @(
    'GUIAPLAY_DIAG_SKIP_DISPLAYCONFIG',
    'GUIAPLAY_DIAG_SKIP_MONITOR_POLL',
    'GUIAPLAY_DIAG_SKIP_STARTUP_UPDATE'
)
$saved = @{}
foreach ($flag in $flags) {
    $saved[$flag] = [Environment]::GetEnvironmentVariable($flag, 'Process')
}

function Add-Result([string]$message) {
    $results.Add($message)
    Write-Host $message
}
function Get-ActiveGuiaplay {
    @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'GuiaPlay.exe'" -ErrorAction Stop)
}
function Run-Case([string]$name, [string[]]$disabled) {
    foreach ($flag in $flags) {
        [Environment]::SetEnvironmentVariable(
            $flag, $(if ($disabled -contains $flag) { '1' } else { $null }), 'Process')
    }
    $started = Get-Date
    Add-Result ''
    Add-Result ('CASE: ' + $name)
    Add-Result ('Flags: ' + ($disabled -join ', '))
    $proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
    $crashed = $false
    $code = $null
    $duration = [Diagnostics.Stopwatch]::StartNew()
    try {
        while ($duration.Elapsed.TotalSeconds -lt $SecondsPerCase) {
            Start-Sleep -Milliseconds 500
            $proc.Refresh()
            if ($proc.HasExited) {
                $code = $proc.ExitCode
                $crashed = $true
                break
            }
        }
        if ($crashed) {
            $hex = '0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$code), 0)
            Add-Result ('RESULT: EXIT ' + $code + ' (' + $hex + ')')
        } else {
            Add-Result ('RESULT: RUNNING after ' + $SecondsPerCase + ' seconds')
        }
    }
    finally {
        if (-not $proc.HasExited) {
            try {
                [void]$proc.CloseMainWindow()
                [void]$proc.WaitForExit(3000)
            } catch { }
            $proc.Refresh()
            if (-not $proc.HasExited) {
                # Only terminate THIS Debug process created by this script.
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                [void]$proc.WaitForExit(3000)
            }
        }
        $proc.Dispose()
    }

    # The Windows crash reporter can write asynchronously after termination.
    Start-Sleep -Seconds 2
    try {
        $events = @(Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            Id = @(1000, 1026)
            StartTime = $started.AddSeconds(-1)
        } -ErrorAction Stop | Where-Object { $_.Message -match 'GuiaPlay.exe' } |
            Sort-Object TimeCreated -Descending | Select-Object -First 3)
        foreach ($event in $events) {
            $module = [regex]::Match($event.Message, '(?im)^(?:Nome do m.dulo com falha|Faulting module name):\s*([^,\r\n]+)').Groups[1].Value
            $exception = [regex]::Match($event.Message, '(?im)(?:C.digo de exce..o|exception code):\s*(0x)?([0-9a-f]{8})').Groups[2].Value
            Add-Result ("WER: event={0} module={1} exception={2}" -f $event.Id, $module, $exception)
        }
    } catch { Add-Result ('WER read: ' + $_.Exception.Message) }
    try {
        $log = Join-Path $env:LOCALAPPDATA 'GuiaSys\GuiaPlay\GuiaPlay.log'
        if (Test-Path -LiteralPath $log) {
            $recent = @(Get-Content $log -Tail 80 | Where-Object {
                $_ -match 'GP-I11:|Painel do operador carregado|Update startup: result'
            } | Select-Object -Last 12)
            foreach ($line in $recent) { Add-Result ('LOG: ' + $line) }
        }
    } catch { Add-Result ('Log read: ' + $_.Exception.Message) }
}
try {
    $already = @(Get-ActiveGuiaplay)
    if ($already.Count -gt 0) {
        throw 'A GuiaPlay.exe instance is already running. Close it normally before isolation.'
    }
    Push-Location $root
    try {
        Add-Result 'GP-I11: compiling the Debug executable once.'
        & dotnet build '.\src\GuiaPlay.App\GuiaPlay.App.csproj' -c Debug
        if ($LASTEXITCODE -ne 0) { throw 'Debug build failed. No diagnostic launch was attempted.' }
        if (-not (Test-Path -LiteralPath $exe)) { throw ('Missing Debug EXE: ' + $exe) }
        Run-Case 'A: no startup update' @('GUIAPLAY_DIAG_SKIP_STARTUP_UPDATE')
        Run-Case 'B: no DisplayConfig native identities' @('GUIAPLAY_DIAG_SKIP_DISPLAYCONFIG')
        Run-Case 'C: no background monitor polling' @('GUIAPLAY_DIAG_SKIP_MONITOR_POLL')
        Run-Case 'D: all 3 isolated' $flags
    } finally {
        Pop-Location
    }
}
finally {
    foreach ($flag in $flags) {
        [Environment]::SetEnvironmentVariable($flag, $saved[$flag], 'Process')
    }
    $path = Join-Path $out 'isolation-result.txt'
    [IO.File]::WriteAllLines($path, [string[]]$results, (New-Object Text.UTF8Encoding($false)))
    Write-Host ('Results saved: ' + $path) -ForegroundColor Green
}
