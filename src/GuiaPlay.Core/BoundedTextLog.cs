using System.Text;

namespace GuiaPlay.Core;

public sealed class BoundedTextLog(string filePath, long maximumFileBytes, int maximumFiles)
{
    private readonly object _gate = new();
    private readonly string _filePath = Path.GetFullPath(filePath);
    private readonly long _maximumFileBytes = Math.Max(1024, maximumFileBytes);
    private readonly int _maximumFiles = Math.Max(1, maximumFiles);

    public void Append(string text)
    {
        lock (_gate)
        {
            RotateIfNeeded();
            File.AppendAllText(_filePath, text + Environment.NewLine, Encoding.UTF8);
        }
    }

    public void RotateIfNeeded()
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("O arquivo de log não possui diretório.");
            Directory.CreateDirectory(directory);
            if (File.Exists(_filePath) && new FileInfo(_filePath).Length >= _maximumFileBytes)
            {
                var stem = Path.GetFileNameWithoutExtension(_filePath);
                var extension = Path.GetExtension(_filePath);
                var archived = Path.Combine(directory, $"{stem}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
                File.Move(_filePath, archived);
            }

            var archivePattern = $"{Path.GetFileNameWithoutExtension(_filePath)}-*{Path.GetExtension(_filePath)}";
            foreach (var stale in new DirectoryInfo(directory)
                         .GetFiles(archivePattern)
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(_maximumFiles - 1))
            {
                stale.Delete();
            }
        }
    }
}
