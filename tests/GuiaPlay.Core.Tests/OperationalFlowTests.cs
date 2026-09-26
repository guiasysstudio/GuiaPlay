using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class OperationalFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M04.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OperatorIndicatorIsStaticAndShowsDisconnectedState()
    {
        var connected = OperatorIndicatorState.Create("Tela Principal", true, true, false);
        var disconnected = OperatorIndicatorState.Create("Tela 1", true, false, true);

        Assert.False(connected.IsInteractive);
        Assert.False(disconnected.IsInteractive);
        Assert.False(connected.IsWarning);
        Assert.True(disconnected.IsWarning);
        Assert.Contains("desconectado", disconnected.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MediaKind.Video, true, 1, PlaylistActivationAction.LoadAndPlay)]
    [InlineData(MediaKind.Video, true, 0, PlaylistActivationAction.LoadOnly)]
    [InlineData(MediaKind.Audio, true, 0, PlaylistActivationAction.LoadAndPlay)]
    [InlineData(MediaKind.Audio, true, 3, PlaylistActivationAction.LoadAndPlay)]
    [InlineData(MediaKind.Video, false, 1, PlaylistActivationAction.None)]
    public void PlaylistDoubleClickUsesCentralActivationPolicy(
        MediaKind kind,
        bool available,
        int outputs,
        PlaylistActivationAction expected)
    {
        Assert.Equal(expected, PlaylistActivationPolicy.Resolve(kind, available, outputs));
    }

    [Fact]
    public void ValidCommandLineMediaIsResolvedWithoutAutoPlayInstruction()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "abertura.mkv");
        File.WriteAllText(path, "test");

        var result = LaunchArgumentParser.Parse([path]);

        Assert.Equal(Path.GetFullPath(path), result.MediaPath);
        Assert.Null(result.Warning);
    }

    [Theory]
    [InlineData("ausente.mp4", "não foi encontrado")]
    [InlineData("invalido.txt", "não foi encontrado")]
    public void MissingCommandLineArgumentIsRejected(string fileName, string warning)
    {
        var result = LaunchArgumentParser.Parse([Path.Combine(_directory, fileName)]);

        Assert.Null(result.MediaPath);
        Assert.Contains(warning, result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedExistingCommandLineArgumentIsRejected()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "notas.txt");
        File.WriteAllText(path, "texto");

        var result = LaunchArgumentParser.Parse([path]);

        Assert.Null(result.MediaPath);
        Assert.Contains("não é suportado", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondInstanceForwardsRequestAndDoesNotBecomePersistentListener()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}");
        using var secondary = new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}");
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(
            SingleInstanceStartResult.Primary,
            await primary.StartAsync(null, path =>
            {
                received.TrySetResult(path);
                return Task.CompletedTask;
            }));

        var secondaryResult = await secondary.StartAsync(@"D:\Midias\Video.mp4", _ => Task.CompletedTask);

        Assert.Equal(SingleInstanceStartResult.Forwarded, secondaryResult);
        Assert.Equal(@"D:\Midias\Video.mp4", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(primary.IsListening);
        Assert.False(secondary.IsListening);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
