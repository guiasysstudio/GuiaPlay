using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace GuiaPlay.App.Services;

internal readonly record struct ProcessLaunchResult(bool Succeeded, string? Error)
{
    public static ProcessLaunchResult Success { get; } = new(true, null);
}

internal static class SafeProcessLauncher
{
    public static ProcessLaunchResult TryOpenShell(string target) =>
        string.IsNullOrWhiteSpace(target)
            ? new ProcessLaunchResult(false, "O destino está vazio.")
            : TryStart(new ProcessStartInfo(target) { UseShellExecute = true });

    public static ProcessLaunchResult TryStart(
        ProcessStartInfo startInfo,
        Func<ProcessStartInfo, Process?>? start = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        try
        {
            using var process = (start ?? Process.Start)(startInfo);
            return process is null
                ? new ProcessLaunchResult(false, "O Windows não iniciou o processo solicitado.")
                : ProcessLaunchResult.Success;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                          IOException or UnauthorizedAccessException or NotSupportedException or
                                          ArgumentException or SecurityException)
        {
            return new ProcessLaunchResult(false, exception.Message);
        }
    }
}
