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

    [Fact]
    public void CommandLineAcceptsSpacesAccentsParenthesesAndUnicode()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "Vídeo 01 (João) 🎵.mkv");
        File.WriteAllText(path, "test");

        var result = LaunchArgumentParser.Parse([path]);

        Assert.Equal(Path.GetFullPath(path), result.MediaPath);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void MultipleArgumentsHaveExplicitSingleFileBehavior()
    {
        Directory.CreateDirectory(_directory);
        var first = Path.Combine(_directory, "primeiro.mp4");
        var second = Path.Combine(_directory, "segundo.mp3");
        File.WriteAllText(first, "test");
        File.WriteAllText(second, "test");

        var result = LaunchArgumentParser.Parse([first, second]);

        Assert.Equal(Path.GetFullPath(first), result.MediaPath);
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

    [Theory]
    [InlineData("script.bat")]
    [InlineData("comando.cmd")]
    [InlineData("automacao.ps1")]
    [InlineData("programa.exe")]
    public void ExecutableOrScriptArgumentsAreNeverAcceptedAsMedia(string fileName)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "unsafe");

        var result = LaunchArgumentParser.Parse([path]);

        Assert.Null(result.MediaPath);
        Assert.Contains("não é suportado", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableNetworkPathIsReportedAsMissing()
    {
        var result = LaunchArgumentParser.Parse([@"\\SERVIDOR-INEXISTENTE\Midias\video.mp4"]);

        Assert.Null(result.MediaPath);
        Assert.Contains("não foi encontrado", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncCommandLineValidationUsesBoundedProbeWithoutBlockingCaller()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "abertura assíncrona.mp4");
        await File.WriteAllTextAsync(path, "test");

        var result = await LaunchArgumentParser.ParseAsync([path], TimeSpan.FromSeconds(1));

        Assert.Equal(Path.GetFullPath(path), result.MediaPath);
        Assert.Null(result.Warning);
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

    [Fact]
    public async Task ExistingInstanceReceivesUnicodeMediaAndSecondInstanceExits()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "Abertura João (Final).mp4");
        File.WriteAllText(path, "test");
        var suffix = Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}");
        using var secondary = new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}");
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(SingleInstanceStartResult.Primary, await primary.StartAsync(null, forwarded =>
        {
            received.TrySetResult(forwarded);
            return Task.CompletedTask;
        }));

        var result = await secondary.StartAsync(path, _ => Task.CompletedTask);

        Assert.Equal(SingleInstanceStartResult.Forwarded, result);
        Assert.Equal(path, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(primary.IsListening);
        Assert.False(secondary.IsListening);
    }

    [Fact]
    public async Task NewCoordinatorForwardsToAlreadyRunningLegacyInstance()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var legacy = new SingleInstanceEndpoint($"GuiaPlay.Tests.Legacy.{suffix}", $"GuiaPlay.Tests.Legacy.Pipe.{suffix}");
        using var legacyInstance = new SingleInstanceCoordinator(legacy.MutexName, legacy.PipeName);
        using var newInstance = new SingleInstanceCoordinator(
            $"GuiaPlay.Tests.Current.{suffix}",
            $"GuiaPlay.Tests.Current.Pipe.{suffix}",
            [legacy]);
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(SingleInstanceStartResult.Primary, await legacyInstance.StartAsync(null, path =>
        {
            received.TrySetResult(path);
            return Task.CompletedTask;
        }));

        var result = await newInstance.StartAsync(@"C:\Mídia\legado.mp4", _ => Task.CompletedTask);

        Assert.Equal(SingleInstanceStartResult.Forwarded, result);
        Assert.Equal(@"C:\Mídia\legado.mp4", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(newInstance.IsListening);
    }

    [Fact]
    public async Task NewCoordinatorKeepsLegacyEndpointSoOldVersionCannotStartBesideIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var legacy = new SingleInstanceEndpoint($"GuiaPlay.Tests.Legacy.{suffix}", $"GuiaPlay.Tests.Legacy.Pipe.{suffix}");
        using var newInstance = new SingleInstanceCoordinator(
            $"GuiaPlay.Tests.Current.{suffix}",
            $"GuiaPlay.Tests.Current.Pipe.{suffix}",
            [legacy]);
        using var legacyInstance = new SingleInstanceCoordinator(legacy.MutexName, legacy.PipeName);
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(SingleInstanceStartResult.Primary, await newInstance.StartAsync(null, path =>
        {
            received.TrySetResult(path);
            return Task.CompletedTask;
        }));

        var result = await legacyInstance.StartAsync(@"C:\Mídia\antigo.mp4", _ => Task.CompletedTask);

        Assert.Equal(SingleInstanceStartResult.Forwarded, result);
        Assert.Equal(@"C:\Mídia\antigo.mp4", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(legacyInstance.IsListening);
    }

    [Fact]
    public async Task RapidConcurrentExecutionsAreForwardedAndProcessedSerially()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}");
        var secondaries = Enumerable.Range(0, 12)
            .Select(_ => new SingleInstanceCoordinator($"GuiaPlay.Tests.{suffix}", $"GuiaPlay.Tests.Pipe.{suffix}"))
            .ToArray();
        var received = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeHandlers = 0;
        var maximumConcurrentHandlers = 0;
        try
        {
            Assert.Equal(SingleInstanceStartResult.Primary, await primary.StartAsync(null, async path =>
            {
                var active = Interlocked.Increment(ref activeHandlers);
                maximumConcurrentHandlers = Math.Max(maximumConcurrentHandlers, active);
                await Task.Delay(10);
                lock (received)
                {
                    received.Add(path!);
                    if (received.Count == secondaries.Length)
                    {
                        completed.TrySetResult();
                    }
                }

                Interlocked.Decrement(ref activeHandlers);
            }));

            var results = await Task.WhenAll(secondaries.Select((secondary, index) =>
                secondary.StartAsync($@"C:\Mídia\arquivo-{index}.mp4", _ => Task.CompletedTask)));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.All(results, result => Assert.Equal(SingleInstanceStartResult.Forwarded, result));
            Assert.Equal(1, maximumConcurrentHandlers);
            Assert.Equal(12, received.Distinct(StringComparer.Ordinal).Count());
            Assert.True(primary.IsListening);
            Assert.All(secondaries, secondary => Assert.False(secondary.IsListening));
        }
        finally
        {
            foreach (var secondary in secondaries)
            {
                secondary.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
