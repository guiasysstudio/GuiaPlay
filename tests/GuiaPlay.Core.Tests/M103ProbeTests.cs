using System.Diagnostics;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class BoundedMediaProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M103.Probes", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BatchProbePreservesInputOrderAndClassifiesEachPath()
    {
        Directory.CreateDirectory(_directory);
        var first = Path.Combine(_directory, "primeiro.mp4");
        var second = Path.Combine(_directory, "segundo.mp3");
        await File.WriteAllTextAsync(first, "fixture");
        await File.WriteAllTextAsync(second, "fixture");
        var missing = Path.Combine(_directory, "ausente.mkv");

        var results = await MediaFileProbe.ProbeManyAsync(
            [first, missing, second, Path.Combine(_directory, "notas.txt")],
            TimeSpan.FromSeconds(2));

        Assert.Equal([first, missing, second, Path.Combine(_directory, "notas.txt")], results.Select(result => result.Path));
        Assert.Equal(
            [MediaProbeStatus.Available, MediaProbeStatus.Missing, MediaProbeStatus.Available, MediaProbeStatus.Unsupported],
            results.Select(result => result.Status));
    }

    [Fact]
    public async Task DedicatedExecutorBoundsHungCallsAndCallersObserveTimeout()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new CountdownEvent(2);
        var active = 0;
        var maximumActive = 0;
        using var executor = new BoundedFileProbeExecutor(
            workerCount: 2,
            queueCapacity: 2,
            _ =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, current);
                entered.Signal();
                release.Wait();
                Interlocked.Decrement(ref active);
                return true;
            });

        var first = ObserveTimeoutAsync(executor.ExistsAsync("first", TimeSpan.FromMilliseconds(100)));
        var second = ObserveTimeoutAsync(executor.ExistsAsync("second", TimeSpan.FromMilliseconds(100)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var stopwatch = Stopwatch.StartNew();
        var queuedOrRejected = Enumerable.Range(0, 20)
            .Select(index => ObserveTimeoutAsync(executor.ExistsAsync($"queued-{index}", TimeSpan.FromMilliseconds(50))))
            .ToArray();

        Assert.True(await first);
        Assert.True(await second);
        Assert.All(await Task.WhenAll(queuedOrRejected), Assert.True);
        stopwatch.Stop();
        Assert.Equal(2, maximumActive);
        Assert.Equal(2, executor.WorkerCount);
        Assert.Equal(2, executor.QueueCapacity);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        release.Set();
    }

    [Fact]
    public async Task CancellationReturnsPromptlyWhileDedicatedWorkerFinishesSafely()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var executor = new BoundedFileProbeExecutor(1, 1, _ =>
        {
            entered.Set();
            release.Wait();
            return true;
        });
        using var cancellation = new CancellationTokenSource();
        var pending = executor.ExistsAsync("blocked", TimeSpan.FromSeconds(30), cancellation.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        release.Set();
    }

    [Fact]
    public async Task AlreadyCancelledBatchDoesNotStartStaleWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MediaFileProbe.ProbeManyAsync([@"C:\mídia.mp4"], TimeSpan.FromSeconds(1), cancellation.Token));
    }

    private static async Task<bool> ObserveTimeoutAsync(Task<bool> task)
    {
        try
        {
            _ = await task;
            return false;
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var snapshot = Volatile.Read(ref maximum);
        while (value > snapshot)
        {
            var previous = Interlocked.CompareExchange(ref maximum, value, snapshot);
            if (previous == snapshot)
            {
                return;
            }

            snapshot = previous;
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
