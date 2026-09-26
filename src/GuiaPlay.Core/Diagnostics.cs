using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GuiaPlay.Core;

public sealed record ProcessResourceSnapshot(
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedMemoryBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double? CpuPercent);

public sealed class ProcessMetricsSampler
{
    private readonly object _gate = new();
    private TimeSpan? _previousCpuTime;
    private long? _previousTimestamp;

    public ProcessResourceSnapshot Sample()
    {
        lock (_gate)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var timestamp = Stopwatch.GetTimestamp();
            var cpuTime = process.TotalProcessorTime;
            double? cpuPercent = null;
            if (_previousCpuTime is { } previousCpu && _previousTimestamp is { } previousTimestamp)
            {
                var elapsed = Stopwatch.GetElapsedTime(previousTimestamp, timestamp);
                if (elapsed > TimeSpan.Zero)
                {
                    cpuPercent = Math.Clamp(
                        (cpuTime - previousCpu).TotalMilliseconds /
                        elapsed.TotalMilliseconds /
                        Math.Max(1, Environment.ProcessorCount) * 100d,
                        0d,
                        100d);
                }
            }

            _previousCpuTime = cpuTime;
            _previousTimestamp = timestamp;
            return new ProcessResourceSnapshot(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                cpuPercent);
        }
    }
}

public sealed record DiagnosticSnapshot(
    string Version,
    string OperatingSystem,
    TimeSpan Uptime,
    ProcessResourceSnapshot Resources,
    string PlaybackState,
    string? MediaName,
    MediaKind MediaType,
    int OutputCount,
    long FramesReceived,
    long FramesRendered,
    long FramesReplaced,
    double AverageFrameCopyMilliseconds,
    long MediaSwitches,
    string UpdateState);

public sealed class SessionDiagnostics(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly long _startedTimestamp = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private long _framesReceived;
    private long _framesRendered;
    private long _frameCopyTicks;
    private long _mediaSwitches;

    public void RecordMediaChanged() => Interlocked.Increment(ref _mediaSwitches);
    public void RecordFrameReceived() => Interlocked.Increment(ref _framesReceived);

    public void RecordFrameRendered(TimeSpan copyDuration)
    {
        Interlocked.Increment(ref _framesRendered);
        Interlocked.Add(ref _frameCopyTicks, Math.Max(0, copyDuration.Ticks));
    }

    public DiagnosticSnapshot Capture(
        ProcessResourceSnapshot resources,
        PlaybackStatus playbackStatus,
        string? mediaPath,
        MediaKind mediaType,
        int outputCount,
        string updateState)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentOutOfRangeException.ThrowIfNegative(outputCount);
        var received = Interlocked.Read(ref _framesReceived);
        var rendered = Interlocked.Read(ref _framesRendered);
        var copyTicks = Interlocked.Read(ref _frameCopyTicks);
        return new DiagnosticSnapshot(
            ProductInfo.Version,
            Environment.OSVersion.VersionString,
            _timeProvider.GetElapsedTime(_startedTimestamp),
            resources,
            playbackStatus.ToString(),
            SanitizeMediaName(mediaPath),
            mediaType,
            outputCount,
            received,
            rendered,
            Math.Max(0, received - rendered),
            rendered == 0 ? 0 : TimeSpan.FromTicks(copyTicks).TotalMilliseconds / rendered,
            Interlocked.Read(ref _mediaSwitches),
            string.IsNullOrWhiteSpace(updateState) ? "Não verificado" : updateState);
    }

    private static string? SanitizeMediaName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : name.Replace('\r', ' ').Replace('\n', ' ');
    }
}

public static class DiagnosticReportFormatter
{
    public static string Format(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var builder = new StringBuilder()
            .Append("GuiaPlay ").AppendLine(snapshot.Version)
            .Append("Windows: ").AppendLine(snapshot.OperatingSystem)
            .Append("Uptime: ").AppendLine(FormatUptime(snapshot.Uptime))
            .Append("Playback: ").AppendLine(snapshot.PlaybackState)
            .Append("Media: ").AppendLine(SafeMediaName(snapshot.MediaName))
            .Append("Media type: ").AppendLine(snapshot.MediaType.ToString())
            .Append("Outputs: ").AppendLine(snapshot.OutputCount.ToString(culture))
            .Append("Working set: ").AppendLine(FormatBytes(snapshot.Resources.WorkingSetBytes, culture))
            .Append("Private memory: ").AppendLine(FormatBytes(snapshot.Resources.PrivateMemoryBytes, culture))
            .Append("Managed memory: ").AppendLine(FormatBytes(snapshot.Resources.ManagedMemoryBytes, culture))
            .Append("CPU aproximada: ").AppendLine(snapshot.Resources.CpuPercent is { } cpu ? $"{cpu.ToString("F1", culture)}%" : "coletando")
            .Append("GC collections: ").Append(snapshot.Resources.Gen0Collections).Append('/')
            .Append(snapshot.Resources.Gen1Collections).Append('/').AppendLine(snapshot.Resources.Gen2Collections.ToString(culture))
            .Append("Frames received: ").AppendLine(snapshot.FramesReceived.ToString(culture))
            .Append("Frames rendered: ").AppendLine(snapshot.FramesRendered.ToString(culture))
            .Append("Frames replaced: ").AppendLine(snapshot.FramesReplaced.ToString(culture))
            .Append("Average frame copy: ").Append(snapshot.AverageFrameCopyMilliseconds.ToString("F3", culture)).AppendLine(" ms")
            .Append("Media switches: ").AppendLine(snapshot.MediaSwitches.ToString(culture))
            .Append("Update state: ").Append(snapshot.UpdateState);
        return builder.ToString();
    }

    private static string FormatUptime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static string SafeMediaName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Nenhuma";
        }

        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? "Nenhuma" : name.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string FormatBytes(long bytes, CultureInfo culture) =>
        $"{(Math.Max(0, bytes) / 1024d / 1024d).ToString("F1", culture)} MB";
}
