using System.Globalization;
using System.IO;
using System.Text;

namespace GuiaPlay.App.Services;

internal static class FileLogger
{
    private const long MaximumFileBytes = 5 * 1024 * 1024;
    private const int MaximumFiles = 5;
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuiaSys",
        "GuiaPlay");
    private static string LogPath => Path.Combine(DirectoryPath, "GuiaPlay.log");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            RotateIfNeeded();
            Info($"GuiaPlay {GuiaPlay.Core.ProductInfo.Version} iniciado.");
        }
        catch
        {
            // Logging must never prevent playback.
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERRO", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                RotateIfNeeded();
                var text = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}";
                if (exception is not null)
                {
                    text += $"{Environment.NewLine}{exception}";
                }

                File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Best-effort local diagnostics only.
        }
    }

    private static void RotateIfNeeded()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaximumFileBytes)
        {
            var archived = Path.Combine(DirectoryPath, $"GuiaPlay-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.Move(LogPath, archived);
        }

        foreach (var stale in new DirectoryInfo(DirectoryPath)
                     .GetFiles("GuiaPlay-*.log")
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(MaximumFiles - 1))
        {
            stale.Delete();
        }
    }
}
