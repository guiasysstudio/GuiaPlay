namespace GuiaPlay.App.Services;

// These switches are diagnostics, not product features. Never ship them in a
// Release build. In particular they do not bypass or modify license checks.
internal static class DevelopmentDiagnostics
{
    public static void Trace(string detail)
    {
#if DEBUG
        FileLogger.Info("GP-I11: " + detail);
#endif
    }

#if DEBUG
    public static bool SkipDisplayConfig => IsEnabled("GUIAPLAY_DIAG_SKIP_DISPLAYCONFIG");
    public static bool SkipMonitorPolling => IsEnabled("GUIAPLAY_DIAG_SKIP_MONITOR_POLL");
    public static bool SkipStartupUpdate => IsEnabled("GUIAPLAY_DIAG_SKIP_STARTUP_UPDATE");

    private static bool IsEnabled(string key) =>
        string.Equals(Environment.GetEnvironmentVariable(key), "1", StringComparison.Ordinal);
#else
    public static bool SkipDisplayConfig => false;
    public static bool SkipMonitorPolling => false;
    public static bool SkipStartupUpdate => false;
#endif
}
