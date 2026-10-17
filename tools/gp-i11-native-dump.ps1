#Requires -Version 5.1
<#
.SYNOPSIS
  Capture one unhandled native GuiaPlay crash without touching production.
.DESCRIPTION
  Uses the Microsoft Sysinternals ProcDump tool. Downloads official signed
  Microsoft binaries only if -DownloadOfficial is passed. No installer,
  registry modifications, auto-update, GitHub Release, telemetry or upload.
  The command does NOT auto-accept the ProcDump license.
  Dumps can contain license keys and other private information: never upload
  the .dmp without reviewing redaction/security implications.
#>
[CmdletBinding()]
param(
    [switch]$DownloadOfficial,
    [string]$ProcDumpPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out = Join-Path $root 'artifacts/gp-i11-native-dump'
$toolsDir = Join-Path $out 'tools'
$dumpDir = Join-Path $out 'dumps'
$exe = Join-Path $root 'src/GuiaPlay.App/bin/Debug/net10.0-windows/win-x64/GuiaPlay.exe'
New-Item -ItemType Directory -Path $toolsDir, $dumpDir -Force | Out-Null
$already = @(Get-CimInstance Win32_Process -Filter "Name='GuiaPlay.exe'" -ErrorAction Stop)
if ($already.Count -gt 0) { throw 'Close existing GuiaPlay instances before capturing a Debug crash.' }
if (-not (Test-Path -LiteralPath $exe)) {
    throw 'Debug binary missing. Run: dotnet build .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug'
}
if (-not $ProcDumpPath) {
    $known = Get-Command procdump64.exe -ErrorAction SilentlyContinue
    if ($null -ne $known) { $ProcDumpPath = $known.Source }
}
if (-not $ProcDumpPath) {
    $local = Join-Path $toolsDir 'procdump64.exe'
    if (Test-Path -LiteralPath $local) { $ProcDumpPath = $local }
}
if (-not $ProcDumpPath -and $DownloadOfficial) {
    $zip = Join-Path $out 'Procdump-Microsoft.zip'
    $url = 'https://download.sysinternals.com/files/Procdump.zip'
    Write-Host ('Downloading official Microsoft tool: ' + $url)
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -ErrorAction Stop
    Expand-Archive -LiteralPath $zip -DestinationPath $toolsDir -Force
    $ProcDumpPath = Join-Path $toolsDir 'procdump64.exe'
}
if (-not $ProcDumpPath -or -not (Test-Path -LiteralPath $ProcDumpPath)) {
    throw 'ProcDump not found. Rerun with -DownloadOfficial (downloads from Microsoft), or provide -ProcDumpPath.'
}
$signature = Get-AuthenticodeSignature -FilePath $ProcDumpPath
if ($signature.Status -ne 'Valid' -or
    $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    throw 'ProcDump Authenticode signature verification failed. Refusing to execute this binary.'
}
Write-Host ('Verified Microsoft ProcDump: ' + $ProcDumpPath) -ForegroundColor Green
Write-Host ('Local private dump directory: ' + $dumpDir)
Write-Warning 'A mini dump can contain private information, including license material. Never upload it unreviewed.'
Write-Host 'ProcDump will start GuiaPlay Debug and monitor unhandled native exceptions.'
Push-Location (Split-Path -Parent $exe)
try {
    & $ProcDumpPath -e -mm -x $dumpDir $exe
    Write-Host ('ProcDump exit code: ' + $LASTEXITCODE)
}
finally { Pop-Location }
$dumps = @(Get-ChildItem -LiteralPath $dumpDir -Filter '*.dmp' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending)
if ($dumps.Count -eq 0) {
    Write-Host 'No native crash dump captured.'
} else {
    $latest = $dumps[0]
    Write-Host ('Captured dump (KEEP PRIVATE): ' + $latest.FullName) -ForegroundColor Yellow
    $cdbCandidates = @(
        'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe',
        'C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe'
    )
    $cdb = $cdbCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($cdb) {
        $analysis = Join-Path $out 'native-stack.txt'
        $commands = '.symfix; .reload; !analyze -v; kb; q'
        Write-Host 'Windows Debugger detected. Extracting symbolic native stack locally.'
        & $cdb -z $latest.FullName -logo $analysis -c $commands
        Write-Host ('Text analysis: ' + $analysis) -ForegroundColor Green
    } else {
        Write-Host 'For a readable stack, open the dump in Microsoft WinDbg and run: !analyze -v; kb'
    }
}
