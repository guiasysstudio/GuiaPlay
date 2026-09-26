[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\artifacts\publish\GuiaPlay\GuiaPlay.exe'),
    [int]$DurationMinutes = 30,
    [int]$SampleSeconds = 5,
    [string]$OutputPath,
    [switch]$Launch,
    [switch]$CloseWhenDone
)

$ErrorActionPreference = 'Stop'
if ($DurationMinutes -lt 1) { throw 'DurationMinutes deve ser pelo menos 1.' }
if ($SampleSeconds -lt 2) { throw 'SampleSeconds deve ser pelo menos 2 para evitar amostragem agressiva.' }

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $PSScriptRoot "..\artifacts\soak\GuiaPlay-soak-$stamp.csv"
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
[IO.Directory]::CreateDirectory($outputDirectory)

$startedHere = $false
if ($Launch) {
    $resolvedExe = [IO.Path]::GetFullPath($ExePath)
    if (-not [IO.File]::Exists($resolvedExe)) { throw "GuiaPlay não encontrado em $resolvedExe" }
    $process = Start-Process -FilePath $resolvedExe -PassThru
    $startedHere = $true
    Start-Sleep -Seconds 2
} else {
    $process = Get-Process -Name 'GuiaPlay' -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
    if ($null -eq $process) { throw 'Nenhum processo GuiaPlay foi encontrado. Use -Launch ou abra o aplicativo antes.' }
}

$samples = [Collections.Generic.List[object]]::new()
$deadline = [DateTimeOffset]::Now.AddMinutes($DurationMinutes)
$previousTimestamp = [DateTimeOffset]::Now
$previousCpu = $process.TotalProcessorTime

try {
    while ([DateTimeOffset]::Now -lt $deadline -and -not $process.HasExited) {
        Start-Sleep -Seconds $SampleSeconds
        $process.Refresh()
        if ($process.HasExited) { break }

        $now = [DateTimeOffset]::Now
        $cpu = $process.TotalProcessorTime
        $elapsedSeconds = [Math]::Max(0.001, ($now - $previousTimestamp).TotalSeconds)
        $cpuPercent = (($cpu - $previousCpu).TotalSeconds / $elapsedSeconds / [Environment]::ProcessorCount) * 100
        $samples.Add([pscustomobject]@{
            Timestamp = $now.ToString('O')
            UptimeSeconds = [Math]::Round(($now - $process.StartTime).TotalSeconds, 1)
            CpuPercent = [Math]::Round([Math]::Max(0, $cpuPercent), 2)
            WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateMemoryMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
            Responding = $process.Responding
        })
        $samples | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8
        $previousTimestamp = $now
        $previousCpu = $cpu
    }
} finally {
    if ($CloseWhenDone -and $startedHere -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        [void]$process.WaitForExit(10000)
    }
}

Write-Output "Amostras: $($samples.Count)"
Write-Output "CSV: $resolvedOutput"
if ($samples.Count -gt 0) {
    $working = $samples | Measure-Object WorkingSetMB -Minimum -Maximum -Average
    $private = $samples | Measure-Object PrivateMemoryMB -Minimum -Maximum -Average
    $cpu = $samples | Measure-Object CpuPercent -Average -Maximum
    Write-Output ("Working Set MB: início={0}; final={1}; mínimo={2}; máximo={3}" -f $samples[0].WorkingSetMB, $samples[-1].WorkingSetMB, $working.Minimum, $working.Maximum)
    Write-Output ("Private MB: início={0}; final={1}; mínimo={2}; máximo={3}" -f $samples[0].PrivateMemoryMB, $samples[-1].PrivateMemoryMB, $private.Minimum, $private.Maximum)
    Write-Output ("CPU aproximada: média={0:N2}%; pico={1:N2}%" -f $cpu.Average, $cpu.Maximum)
}
