#Requires -Version 5.1
<#
.SYNOPSIS
  Diagnose a Guiaplay startup crash without changing program settings.
.DESCRIPTION
  Collects Windows Application Error / Windows Error Reporting events.
  By default it runs Guiaplay from VS Code using dotnet run, then reads events.
  -AnalyzeOnly reads prior events without launching the application.
  No elevated privileges, registry changes, installers, uploads or dumps.
  ASCII source for Windows PowerShell 5.1 compatibility.
#>
[CmdletBinding()]
param([switch]$AnalyzeOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reportDir = Join-Path $root 'artifacts/gp-i11-startup-diagnostics'
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$report = Join-Path $reportDir 'crash-report.txt'

$buffer = New-Object 'System.Collections.Generic.List[string]'
function Add-Report {
    param([string]$Text)
    $buffer.Add($Text)
    Write-Host $Text
}

Add-Report ('GuiaPlay GP-I11 startup diagnostics: ' + (Get-Date).ToString('o'))
Add-Report ('Workspace: ' + $root)

try {
    $existing = @(Get-CimInstance Win32_Process -Filter "Name = 'GuiaPlay.exe'" -ErrorAction Stop)
    foreach ($process in $existing) {
        Add-Report ("Already running: PID={0}, path={1}" -f $process.ProcessId, $process.ExecutablePath)
    }
} catch {
    Add-Report ('Unable to inspect running processes: ' + $_.Exception.Message)
    $existing = @()
}

$started = (Get-Date).AddHours(-6)

if (-not $AnalyzeOnly) {
    if ($existing.Count -gt 0) {
        Add-Report 'STOP: Close any existing Guiaplay window normally, then run this script again.'
        [System.IO.File]::WriteAllLines($report, [string[]]$buffer)
        return
    }

    $started = Get-Date
    Add-Report ('Starting Debug at ' + $started.ToString('o'))
    Push-Location $root
    try {
        & dotnet run --project '.\src\GuiaPlay.App\GuiaPlay.App.csproj' -c Debug
        $exitCode = $LASTEXITCODE
        if ($null -ne $exitCode) {
            $exitHex = '0x{0:X8}' -f ([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$exitCode), 0))
            Add-Report ("Process exit: {0} ({1})" -f $exitCode, $exitHex)
        } else {
            Add-Report 'dotnet run returned no process exit code.'
        }
    } finally {
        Pop-Location
    }
    Start-Sleep -Seconds 2
}

Add-Report ''
Add-Report '=== Recent Application Error / WER / .NET Runtime events ==='
$fromTime = $started.AddMinutes(-3)
if ($AnalyzeOnly) { $fromTime = (Get-Date).AddHours(-12) }
try {
    $recent = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        Id = @(1000, 1001, 1026)
        StartTime = $fromTime
    } -ErrorAction Stop | Where-Object {
        $_.Message -match 'GuiaPlay|libvlc|dotnet\.exe'
    } | Sort-Object TimeCreated -Descending | Select-Object -First 8)

    if ($recent.Count -eq 0) {
        Add-Report 'No matching Windows Application events in the selected window.'
    }

    foreach ($ev in $recent) {
        Add-Report ("Time={0}; EventId={1}; Source={2}" -f
            $ev.TimeCreated.ToString('o'), $ev.Id, $ev.ProviderName)
        Add-Report $ev.Message
        Add-Report '---'
    }
} catch {
    Add-Report ('Could not read Windows Application events: ' + $_.Exception.Message)
}

Add-Report ''
Add-Report '=== Last current GuiaPlay log lines ==='
$log = Join-Path $env:LOCALAPPDATA 'GuiaSys\GuiaPlay\GuiaPlay.log'
if (Test-Path -LiteralPath $log) {
    $logLines = @(Get-Content -LiteralPath $log -Tail 16 -ErrorAction SilentlyContinue)
    foreach ($line in $logLines) { Add-Report $line }
} else {
    Add-Report 'No shared GuiaPlay.log was found.'
}

[System.IO.File]::WriteAllLines(
    $report, [string[]]$buffer, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host ('Local report saved: ' + $report) -ForegroundColor Green
Write-Host 'Share the faulting application, faulting module, exception code and event time.'
Write-Host 'Redact user paths, machine details and any sensitive data before sharing.'
