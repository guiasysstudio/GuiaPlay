using System.Security.Cryptography;
using System.Text;

namespace GuiaPlay.Core;

public sealed record FolderMediaCandidate(string RelativeFolder, string FullPath, MediaKind Kind);

public sealed record FolderMediaScanResult(
    string RootName,
    IReadOnlyList<FolderMediaCandidate> Candidates,
    int DirectoriesVisited,
    int SkippedUnsupported,
    int SkippedUnsafe,
    bool Limited,
    IReadOnlyList<string> Warnings)
{
    public string RootPath { get; init; } = string.Empty;
}

public sealed record FolderMediaMergeResult(
    PlaylistDocument Playlist,
    int Added,
    int SkippedDuplicates,
    int GroupsCreated);

/// <summary>
/// Read-only, bounded and cancellable directory enumeration. Reparse points are never followed.
/// Paths are references; no media bytes are copied, renamed or deleted.
/// </summary>
public static class FolderMediaScanner
{
    public const int MaxCandidates = 3000;
    public const int MaxDirectories = 320;
    public const int MaxDepth = 24;
    public const int MaxEntries = 12000;

    public static Task<FolderMediaScanResult> ScanAsync(string folder, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Task.Run(() => Scan(folder, token), token);
    }

    private static FolderMediaScanResult Scan(string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(folder);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException($"A pasta não foi encontrada: {root}");
        }

        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A seleção de atalhos/junções de pastas não é permitida.");
        }

        var candidates = new List<FolderMediaCandidate>();
        var warnings = new List<string>();
        var pending = new Stack<(string Directory, string RelativeFolder, int Depth)>();
        pending.Push((root, string.Empty, 0));
        var visited = 0;
        var entriesVisited = 0;
        var skippedUnsupported = 0;
        var skippedUnsafe = 0;
        var limited = false;

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            if (visited >= MaxDirectories || entriesVisited >= MaxEntries ||
                candidates.Count >= MaxCandidates)
            {
                limited = true;
                break;
            }

            var current = pending.Pop();
            visited++;
            var directoryEntries = new List<string>();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(current.Directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (entriesVisited + directoryEntries.Count >= MaxEntries)
                    {
                        limited = true;
                        break;
                    }

                    directoryEntries.Add(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (warnings.Count < 20)
                {
                    warnings.Add($"Pasta ignorada: {current.Directory} ({exception.Message})");
                }

                skippedUnsafe++;
                continue;
            }

            directoryEntries.Sort(StringComparer.OrdinalIgnoreCase);
            var subfolders = new List<(string Directory, string RelativeFolder, int Depth)>();
            foreach (var path in directoryEntries)
            {
                token.ThrowIfCancellationRequested();
                if (++entriesVisited > MaxEntries)
                {
                    limited = true;
                    break;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skippedUnsafe++;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedUnsafe++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (current.Depth >= MaxDepth)
                    {
                        limited = true;
                        continue;
                    }

                    var relative = string.IsNullOrEmpty(current.RelativeFolder)
                        ? Path.GetFileName(path)
                        : Path.Combine(current.RelativeFolder, Path.GetFileName(path));
                    subfolders.Add((path, relative, current.Depth + 1));
                    continue;
                }

                var kind = MediaTypeDetector.Detect(path);
                if (kind == MediaKind.Unknown)
                {
                    skippedUnsupported++;
                    continue;
                }

                candidates.Add(new FolderMediaCandidate(current.RelativeFolder, path, kind));
                if (candidates.Count >= MaxCandidates)
                {
                    limited = true;
                    break;
                }
            }

            // Stack is LIFO: reverse pushing guarantees stable alphabetical traversal.
            for (var i = subfolders.Count - 1; i >= 0; i--)
            {
                pending.Push(subfolders[i]);
            }

            if (limited)
            {
                break;
            }
        }

        return new FolderMediaScanResult(
            rootInfo.Name, candidates, visited, skippedUnsupported,
            skippedUnsafe, limited, warnings)
        {
            RootPath = root
        };
    }
}

/// <summary>
/// Pure snapshot merge for folder imports. No filesystem writes and no mutations of the
/// existing catalog occur until the caller has persisted the returned document.
/// </summary>
public static class FolderMediaPlaylistMerge
{
    public static FolderMediaMergeResult Merge(PlaylistDocument current, FolderMediaScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(scan);
        var groups = current.Groups
            .Select(group => group with { Items = group.Items.ToArray() })
            .ToList();
        var existingPaths = new HashSet<string>(
            groups.SelectMany(group => group.Items).Select(item => item.OriginalPath),
            StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var duplicates = 0;
        var created = 0;

        foreach (var entry in scan.Candidates)
        {
            if (!existingPaths.Add(entry.FullPath))
            {
                duplicates++;
                continue;
            }

            var directory = string.IsNullOrEmpty(scan.RootPath) ? null
                : Path.GetFullPath(Path.Combine(scan.RootPath, entry.RelativeFolder));
            var name = GroupName(scan.RootName, entry.RelativeFolder);
            // Linked Windows folders are matched by identity of their source path,
            // not merely by display name; empty links work too.
            var index = directory is null ? -1 : groups.FindIndex(group =>
                string.Equals(group.SourceDirectory, directory, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && !string.IsNullOrEmpty(scan.RootPath) && groups.Any(group =>
                string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (directory is not null && group.SourceDirectory is null && group.Items.Count == 0 ||
                 group.SourceDirectory is not null && !string.Equals(group.SourceDirectory,
                     directory, StringComparison.OrdinalIgnoreCase) ||
                 group.Items.Count > 0 && group.Items.Any(item =>
                    !BelongsToRoot(item.OriginalPath, scan.RootPath)))))
            {
                name = MakeBoundedName(name, scan.RootPath);
            }

            if (index < 0)
            {
                index = groups.FindIndex(group =>
                    string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    (group.SourceDirectory is null || directory is null ||
                     string.Equals(group.SourceDirectory, directory, StringComparison.OrdinalIgnoreCase)));
            }

            if (index < 0)
            {
                index = groups.Count;
                groups.Add(new PlaylistGroup(Guid.NewGuid(), name, index, [], directory));
                created++;
            }

            var group = groups[index];
            var items = group.Items.ToList();
            items.Add(new PlaylistItem(
                Guid.NewGuid(),
                Path.GetFileName(entry.FullPath),
                entry.FullPath,
                entry.Kind,
                items.Count));
            groups[index] = group with { Items = items };
            added++;
        }

        return new FolderMediaMergeResult(
            new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, groups),
            added, duplicates, created);
    }

    public static string GroupName(string rootName, string relativeFolder)
    {
        var readable = string.IsNullOrWhiteSpace(relativeFolder)
            ? rootName
            : $"{rootName} / {relativeFolder.Replace('\\', '/').Replace("/", " / ")}";
        return PlaylistGroupName.IsValid(readable)
            ? readable
            : MakeBoundedName(readable, readable);
    }

    private static string MakeBoundedName(string readable, string sourceKey)
    {
        // Stable suffix disambiguates deep folders and identical labels from
        // other media roots while respecting the 80-character schema limit.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey.ToUpperInvariant()));
        var suffix = Convert.ToHexString(digest.AsSpan(0, 5));
        var maxPrefix = 80 - suffix.Length - 1;
        return $"{readable[..Math.Min(maxPrefix, readable.Length)].TrimEnd()}~{suffix}";
    }

    private static bool BelongsToRoot(string filePath, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
