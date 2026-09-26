using System.Globalization;
using System.IO;
using GuiaPlay.Core;

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
    private static readonly BoundedTextLog Log = new(LogPath, MaximumFileBytes, MaximumFiles);

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Log.RotateIfNeeded();
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
                var text = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}";
                if (exception is not null)
                {
                    text += $"{Environment.NewLine}{exception}";
                }

                Log.Append(text);
            }
        }
        catch
        {
            // Best-effort local diagnostics only.
        }
    }

}
