using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class YouTubeVideoReferenceTests
{
    private const string Id = "M7lc1UVf-VE";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VE&t=20")]
    [InlineData("https://m.youtube.com/watch?list=PL123&v=M7lc1UVf-VE")]
    [InlineData("https://music.youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtu.be/M7lc1UVf-VE")]
    [InlineData("https://youtu.be/M7lc1UVf-VE?t=20")]
    [InlineData("https://www.youtube.com/shorts/M7lc1UVf-VE")]
    [InlineData("https://youtube.com/live/M7lc1UVf-VE")]
    [InlineData("https://www.youtube.com/embed/M7lc1UVf-VE")]
    [InlineData("https://www.youtube-nocookie.com/embed/M7lc1UVf-VE")]
    public void AcceptsKnownVideoFormsAndNormalizesIdentity(string url)
    {
        Assert.True(YouTubeVideoReferenceParser.TryParse(url, out var video));
        Assert.NotNull(video);
        Assert.Equal(Id, video.VideoId);
        Assert.Equal($"https://www.youtube.com/watch?v={Id}", video.WatchUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("M7lc1UVf-VE")]
    [InlineData("http://youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com.evil.example/watch?v=M7lc1UVf-VE")]
    [InlineData("https://notyoutube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com:8443/watch?v=M7lc1UVf-VE")]
    [InlineData("https://user:pass@youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE&v=abcdefghijk")]
    [InlineData("https://www.youtube.com/channel/M7lc1UVf-VE")]
    [InlineData("https://www.youtube.com/playlist?list=M7lc1UVf-VE")]
    [InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE%0A")]
    [InlineData("https://youtu.be/M7lc1UVf-VE/more")]
    [InlineData("https://www.youtube.com/embed/M7lc1UVf-VE/more")]
    [InlineData("https://www.youtube.com/watch?v=aaaaaaaaaaa%26evil=1")]
    public void RejectsUnknownUntrustedOrAmbiguousLinks(string? url)
    {
        Assert.False(YouTubeVideoReferenceParser.TryParse(url, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void RejectsExtremelyLongUntrustedLinkWithoutOpeningNetwork()
    {
        var value = "https://youtube.com/watch?v=M7lc1UVf-VE&foo=" + new string('a', 3000);
        Assert.False(YouTubeVideoReferenceParser.TryParse(value, out _));
    }

    [Fact]
    public void EmbedUriPreservesTheOfficialPlayerAndSafeOrigin()
    {
        Assert.True(YouTubeVideoReferenceParser.TryParse(
            "https://youtu.be/M7lc1UVf-VE", out var video));

        var embed = video!.EmbedUri(new Uri("https://guiaplay.assets.example/"));

        Assert.Equal("www.youtube.com", embed.Host);
        Assert.Equal($"/embed/{Id}", embed.AbsolutePath);
        Assert.Contains("enablejsapi=1", embed.Query);
        Assert.Contains("playsinline=1", embed.Query);
        Assert.Contains("origin=https%3A%2F%2Fguiaplay.assets.example", embed.Query);
        Assert.DoesNotContain("autoplay=", embed.Query);
    }

    [Theory]
    [InlineData("http://guiaplay.assets.example/")]
    [InlineData("https://guiaplay.assets.example/page")]
    [InlineData("https://attacker@guiaplay.assets.example/")]
    [InlineData("https://guiaplay.assets.example/?x=1")]
    public void RejectsInvalidEmbeddingOrigins(string origin)
    {
        var reference = new YouTubeVideoReference(Id);
        Assert.Throws<ArgumentException>(() => reference.EmbedUri(new Uri(origin)));
    }

    [Fact]
    public void NullEmbeddingOriginIsRejected()
    {
        var reference = new YouTubeVideoReference(Id);
        Assert.Throws<ArgumentNullException>(() => reference.EmbedUri(null!));
    }
}
