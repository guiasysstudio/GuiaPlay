namespace GuiaPlay.Core;

/// <summary>
/// Only the public YouTube video identity is persisted. This value does not
/// grant permission to download media, bypass embedding restrictions or use VLC.
/// </summary>
public sealed record YouTubeVideoReference(string VideoId)
{
    public Uri WatchUri => new($"https://www.youtube.com/watch?v={VideoId}");

    public Uri EmbedUri(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(origin.Host) || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length > 0 ||
            origin.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "A origem do player precisa ser uma origem HTTPS confiável, sem caminho nem parâmetros.",
                nameof(origin));
        }

        var safeOrigin = Uri.EscapeDataString(origin.GetLeftPart(UriPartial.Authority));
        return new Uri(
            $"https://www.youtube.com/embed/{VideoId}?enablejsapi=1&playsinline=1&origin={safeOrigin}");
    }
}

/// <summary>
/// Parses only explicitly allowed YouTube watch/video URLs. Does not resolve
/// redirects, visit the network, fetch metadata, or interpret video streams.
/// </summary>
public static class YouTubeVideoReferenceParser
{
    private const int MaxLinkLength = 2048;

    public static bool TryParse(string? input, out YouTubeVideoReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxLinkLength ||
            !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Host.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        var hostname = uri.IdnHost.ToLowerInvariant();
        string? id = null;
        if (hostname is "youtu.be" or "www.youtu.be")
        {
            id = SingleSegment(uri.AbsolutePath);
        }
        else if (hostname is "youtube.com" or "www.youtube.com" or
                 "m.youtube.com" or "music.youtube.com" or "www.youtube-nocookie.com")
        {
            if (uri.AbsolutePath.Equals("/watch", StringComparison.Ordinal))
            {
                id = GetVideoQueryValue(uri.Query);
            }
            else
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 2 && segments[0] is "shorts" or "live" or "embed")
                {
                    id = Uri.UnescapeDataString(segments[1]);
                }
            }
        }

        if (!IsValidVideoId(id))
        {
            return false;
        }

        reference = new YouTubeVideoReference(id!);
        return true;
    }

    private static string? SingleSegment(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 ? Uri.UnescapeDataString(segments[0]) : null;
    }

    private static string? GetVideoQueryValue(string query)
    {
        string? candidate = null;
        var found = false;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(key, "v", StringComparison.Ordinal))
            {
                continue;
            }

            if (found || separator < 0)
            {
                return null; // Two video IDs would make the user's intent ambiguous.
            }

            found = true;
            candidate = Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return candidate;
    }

    private static bool IsValidVideoId(string? value)
    {
        if (value is null || value.Length != 11)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }
}
