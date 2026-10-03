using System.Diagnostics;
using System.Runtime.InteropServices;
using GuiaPlay.App.Playback;
using Xunit;

namespace GuiaPlay.App.Tests;

public sealed class FrameBufferRingTests
{
    [Fact]
    public void CommittedFrameCanBeReadWithConfiguredGeometry()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(16, 9, 64, 16));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var token = ring.AcquireWrite(planes);
            Assert.NotEqual(nint.Zero, token);
            Assert.Equal(token, Marshal.ReadIntPtr(planes));
            Marshal.WriteByte(token, 0, 0x5A);
            Assert.True(ring.Commit(token));

            Assert.True(ring.TryAcquireLatest(out var lease));
            using (lease)
            {
                Assert.Equal(token, lease.Pointer);
                Assert.Equal((byte)0x5A, Marshal.ReadByte(lease.Pointer));
                Assert.Equal(16u, lease.Width);
                Assert.Equal(9u, lease.Height);
                Assert.Equal(64u, lease.Pitch);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
        }
    }

    [Fact]
    public void ResetDoesNotWaitForWriterAndLateCommitReleasesRetiredBuffer()
    {
        var messages = new List<string>();
        using var ring = new FrameBufferRing(messages.Add);
        Assert.True(ring.TryConfigure(32, 18, 128, 32));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var token = ring.AcquireWrite(planes);
            Assert.NotEqual(nint.Zero, token);

            var stopwatch = Stopwatch.StartNew();
            ring.Reset();
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(1, ring.AllocatedSlotCount);
            Assert.Equal(1, ring.BusySlotCount);
            Assert.False(ring.Commit(token));
            Assert.Equal(0, ring.AllocatedSlotCount);
            Assert.Contains(messages, message => message.Contains("sem bloquear", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
        }
    }

    [Fact]
    public void ReconfigurationCannotLetAnOldCallbackCommitIntoANewGeneration()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var oldPlanes = Marshal.AllocHGlobal(nint.Size);
        var newPlanes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var oldToken = ring.AcquireWrite(oldPlanes);
            Assert.NotEqual(nint.Zero, oldToken);

            Assert.True(ring.TryConfigure(16, 16, 64, 16));
            var newToken = ring.AcquireWrite(newPlanes);

            Assert.NotEqual(nint.Zero, newToken);
            Assert.NotEqual(oldToken, newToken);
            Assert.False(ring.Commit(oldToken));
            Assert.True(ring.Commit(newToken));
            Assert.True(ring.TryAcquireLatest(out var lease));
            using (lease)
            {
                Assert.Equal(newToken, lease.Pointer);
                Assert.Equal(16u, lease.Width);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(oldPlanes);
            Marshal.FreeHGlobal(newPlanes);
        }
    }

    [Fact]
    public void RetiredReadLeaseKeepsMemoryAliveUntilReleased()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var token = ring.AcquireWrite(planes);
            Assert.True(ring.Commit(token));
            Assert.True(ring.TryAcquireLatest(out var lease));

            ring.Reset();

            Assert.Equal(1, ring.AllocatedSlotCount);
            Marshal.WriteByte(lease.Pointer, 0, 0x2A);
            Assert.Equal((byte)0x2A, Marshal.ReadByte(lease.Pointer));
            lease.Dispose();
            Assert.Equal(0, ring.AllocatedSlotCount);
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
        }
    }

    [Fact]
    public void QuiescentFormatCleanupReclaimsWriterWithoutFinalDisplayCallback()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            Assert.NotEqual(nint.Zero, ring.AcquireWrite(planes));

            ring.CompleteFormatCleanupAfterCallbacksStopped();

            Assert.Equal(0, ring.AllocatedSlotCount);
            Assert.Equal(0, ring.BusySlotCount);
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
        }
    }

    [Fact]
    public void ShutdownRoutesLateWritersToSafeDiscardMemoryUntilQuiescence()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var firstPlanes = Marshal.AllocHGlobal(nint.Size);
        var secondPlanes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            Assert.NotEqual(nint.Zero, ring.AcquireWrite(firstPlanes));

            ring.BeginShutdown();

            Assert.True(ring.IsShutdownRequested);
            var discardToken = ring.AcquireWrite(secondPlanes);
            Assert.NotEqual(nint.Zero, discardToken);
            Assert.Equal(discardToken, Marshal.ReadIntPtr(secondPlanes));
            Marshal.WriteByte(discardToken, 0, 0x33);
            Assert.False(ring.Commit(discardToken));
            Assert.Equal(2, ring.AllocatedSlotCount);
            ring.ReleaseRetiredWritesAfterCallbacksStopped();
            Assert.Equal(0, ring.AllocatedSlotCount);
        }
        finally
        {
            Marshal.FreeHGlobal(firstPlanes);
            Marshal.FreeHGlobal(secondPlanes);
        }
    }

    [Fact]
    public async Task ShutdownImmediatelyUnblocksAWriterWaitingForAllSlots()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Enumerable.Range(0, 4).Select(_ => Marshal.AllocHGlobal(nint.Size)).ToArray();
        try
        {
            foreach (var pointer in planes.Take(3))
            {
                Assert.NotEqual(nint.Zero, ring.AcquireWrite(pointer));
            }

            var waitingWriter = Task.Run(() => ring.AcquireWrite(planes[3]));
            await Task.Delay(30);
            ring.BeginShutdown();

            var discardToken = await waitingWriter.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotEqual(nint.Zero, discardToken);
            Assert.Equal(discardToken, Marshal.ReadIntPtr(planes[3]));
            Assert.False(ring.Commit(discardToken));
            ring.ReleaseRetiredWritesAfterCallbacksStopped();
            Assert.Equal(0, ring.AllocatedSlotCount);
        }
        finally
        {
            foreach (var pointer in planes)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    [Fact]
    public void DisposeDefersManagedReadLeaseInsteadOfFreeingInUseMemory()
    {
        var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var token = ring.AcquireWrite(planes);
            Assert.True(ring.Commit(token));
            Assert.True(ring.TryAcquireLatest(out var lease));

            ring.Dispose();

            Assert.Equal(1, ring.AllocatedSlotCount);
            Marshal.WriteByte(lease.Pointer, 0, 0x7B);
            lease.Dispose();
            Assert.Equal(0, ring.AllocatedSlotCount);
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
            ring.Dispose();
        }
    }

    [Fact]
    public void CopiedLeaseCannotReleaseSlotAfterItWasReusedByANewerFrame()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Marshal.AllocHGlobal(nint.Size);
        try
        {
            var firstToken = ring.AcquireWrite(planes);
            Assert.True(ring.Commit(firstToken));
            Assert.True(ring.TryAcquireLatest(out var firstLease));
            var copiedLease = firstLease;

            firstLease.Dispose();
            var secondToken = ring.AcquireWrite(planes);
            Assert.Equal(firstToken, secondToken);
            Assert.True(ring.Commit(secondToken));
            Assert.True(ring.TryAcquireLatest(out var secondLease));

            copiedLease.Dispose();

            Assert.Equal(1, ring.BusySlotCount);
            Marshal.WriteByte(secondLease.Pointer, 0, 0x4C);
            Assert.Equal((byte)0x4C, Marshal.ReadByte(secondLease.Pointer));
            secondLease.Dispose();
        }
        finally
        {
            Marshal.FreeHGlobal(planes);
        }
    }

    [Fact]
    public void ImpossibleAllocationIsRejectedWithoutPartialNativeBuffers()
    {
        var messages = new List<string>();
        using var ring = new FrameBufferRing(messages.Add);

        Assert.False(ring.TryConfigure(1, 1, uint.MaxValue, uint.MaxValue));

        Assert.Equal(0, ring.AllocatedSlotCount);
        Assert.Contains(messages, message => message.Contains("reservar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WriterWaitIsBoundedAndUsesSafeDiscardMemoryWhenEveryFrameSlotIsBusy()
    {
        using var ring = new FrameBufferRing();
        Assert.True(ring.TryConfigure(8, 8, 32, 8));
        var planes = Enumerable.Range(0, 4).Select(_ => Marshal.AllocHGlobal(nint.Size)).ToArray();
        try
        {
            var tokens = planes.Take(3).Select(ring.AcquireWrite).ToArray();
            Assert.All(tokens, token => Assert.NotEqual(nint.Zero, token));

            var stopwatch = Stopwatch.StartNew();
            var discarded = ring.AcquireWrite(planes[3]);
            stopwatch.Stop();

            Assert.NotEqual(nint.Zero, discarded);
            Assert.Equal(discarded, Marshal.ReadIntPtr(planes[3]));
            Marshal.WriteByte(discarded, 0, 0x6D);
            Assert.False(ring.Commit(discarded));
            Assert.InRange(stopwatch.ElapsedMilliseconds, 150, 1500);
            foreach (var token in tokens)
            {
                Assert.True(ring.Commit(token));
            }
        }
        finally
        {
            foreach (var pointer in planes)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
