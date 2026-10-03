using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GuiaPlay.App.Playback;

/// <summary>
/// Owns the unmanaged video buffers shared by LibVLC callbacks and the WPF renderer.
/// Buffer sets are retired instead of being freed while a native writer or managed reader
/// still owns them. This makes format changes and cleanup non-blocking and prevents both
/// teardown deadlocks and use-after-free.
/// </summary>
internal sealed unsafe class FrameBufferRing : IDisposable
{
    private const int SlotCount = 3;
    internal const uint PictureBufferCount = SlotCount;
    private static readonly TimeSpan WriteAcquireTimeout = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private readonly Dictionary<nint, Slot> _allocatedSlots = [];
    private readonly Action<string>? _abnormalStateLogger;
    private Slot[] _activeSlots = [];
    private Slot? _activeDiscardSlot;
    private Slot? _shutdownDiscardSlot;
    private nuint _lastBufferBytes;
    private long _sequence;
    private bool _shutdownRequested;
    private bool _disposed;

    public FrameBufferRing(Action<string>? abnormalStateLogger = null)
    {
        _abnormalStateLogger = abnormalStateLogger;
    }

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint Pitch { get; private set; }

    internal int AllocatedSlotCount
    {
        get
        {
            lock (_gate)
            {
                return _allocatedSlots.Count;
            }
        }
    }

    internal int BusySlotCount
    {
        get
        {
            lock (_gate)
            {
                return _allocatedSlots.Values.Count(slot => slot.State is SlotState.Reading or SlotState.Writing);
            }
        }
    }

    internal bool IsShutdownRequested
    {
        get
        {
            lock (_gate)
            {
                return _shutdownRequested;
            }
        }
    }

    public bool TryConfigure(uint width, uint height, uint pitch, uint lines)
    {
        if (width == 0 || height == 0 || pitch == 0 || lines == 0)
        {
            LogAbnormal("Configuração de buffer de vídeo rejeitada porque possui dimensão zero.");
            return false;
        }

        lock (_gate)
        {
            if (_shutdownRequested || _disposed)
            {
                return false;
            }

            Slot[] newSlots;
            Slot newDiscardSlot;
            nuint bytes;
            try
            {
                bytes = CalculateBufferSize(pitch, lines);
                var allocated = AllocateSlots(bytes, SlotCount + 1);
                newSlots = allocated[..SlotCount];
                newDiscardSlot = allocated[^1];
                newDiscardSlot.IsDiscard = true;
            }
            catch (Exception exception) when (exception is OverflowException or OutOfMemoryException)
            {
                LogAbnormal($"Não foi possível reservar buffers de vídeo: {exception.Message}");
                return false;
            }

            RetireActiveSlotsWhileLocked("reconfiguração de formato");
            _activeSlots = newSlots;
            _activeDiscardSlot = newDiscardSlot;
            _lastBufferBytes = bytes;
            foreach (var slot in newSlots.Append(newDiscardSlot))
            {
                _allocatedSlots.Add(slot.Pointer, slot);
            }

            Width = width;
            Height = height;
            Pitch = pitch;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public nint AcquireWrite(nint planes)
    {
        if (planes == nint.Zero)
        {
            return nint.Zero;
        }

        var started = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_disposed)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            if (_shutdownRequested)
            {
                return AcquireDiscardWhileLocked(planes, _shutdownDiscardSlot, "shutdown");
            }

            if (_activeSlots.Length == 0)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            var slot = FindWritableSlotWhileLocked();
            while (slot is null && !_shutdownRequested && !_disposed)
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                var remaining = WriteAcquireTimeout - elapsed;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_gate, remaining))
                {
                    LogAbnormal($"Aquisição de buffer de escrita excedeu {WriteAcquireTimeout.TotalMilliseconds:0} ms; quadro descartado.");
                    return AcquireDiscardWhileLocked(planes, _activeDiscardSlot, "saturação do ring");
                }

                slot = FindWritableSlotWhileLocked();
            }

            if (_disposed)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            if (_shutdownRequested)
            {
                return AcquireDiscardWhileLocked(planes, _shutdownDiscardSlot, "shutdown");
            }

            if (slot is null)
            {
                return AcquireDiscardWhileLocked(planes, _activeDiscardSlot, "saturação do ring");
            }

            slot.State = SlotState.Writing;
            Marshal.WriteIntPtr(planes, slot.Pointer);

            // The unmanaged address is unique for the lifetime of the slot and therefore also
            // serves as a generation-safe picture token. An old callback can never commit a
            // newly configured slot merely because both occupied the same array index.
            return slot.Pointer;
        }
    }

    public bool Commit(nint token)
    {
        if (token == nint.Zero)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_allocatedSlots.TryGetValue(token, out var slot) || slot.State != SlotState.Writing)
            {
                return false;
            }

            if (slot.IsDiscard)
            {
                slot.DiscardWriterCount = Math.Max(0, slot.DiscardWriterCount - 1);
                if (slot.DiscardWriterCount == 0)
                {
                    slot.State = SlotState.Free;
                    if (slot.Retired &&
                        (!_shutdownRequested || !ReferenceEquals(slot, _shutdownDiscardSlot)))
                    {
                        FreeSlotWhileLocked(slot);
                    }
                }

                Monitor.PulseAll(_gate);
                return false;
            }

            if (slot.Retired || _shutdownRequested || _disposed)
            {
                slot.State = SlotState.Free;
                FreeSlotWhileLocked(slot);
                Monitor.PulseAll(_gate);
                return false;
            }

            slot.Sequence = ++_sequence;
            slot.State = SlotState.Ready;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool TryAcquireLatest(out FrameLease lease)
    {
        lock (_gate)
        {
            if (_shutdownRequested || _disposed)
            {
                lease = default;
                return false;
            }

            Slot? newest = null;
            foreach (var candidate in _activeSlots)
            {
                if (candidate.State == SlotState.Ready &&
                    (newest is null || candidate.Sequence > newest.Sequence))
                {
                    newest = candidate;
                }
            }

            if (newest is null)
            {
                lease = default;
                return false;
            }

            foreach (var candidate in _activeSlots)
            {
                if (!ReferenceEquals(candidate, newest) && candidate.State == SlotState.Ready)
                {
                    candidate.State = SlotState.Free;
                }
            }

            newest.State = SlotState.Reading;
            lease = new FrameLease(this, newest, newest.Pointer, Width, Height, Pitch);
            return true;
        }
    }

    /// <summary>
    /// Retires the current format without waiting for callbacks or render leases. A subsequent
    /// format callback may configure a fresh set unless shutdown has begun.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            RetireActiveSlotsWhileLocked("limpeza do formato");
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Handles LibVLC's format-cleanup callback. LibVLC invokes cleanup only after callbacks for
    /// that format are quiescent, so abandoned native writers can be reclaimed immediately while
    /// managed read leases remain deferred until their Dispose call.
    /// </summary>
    public void CompleteFormatCleanupAfterCallbacksStopped()
    {
        lock (_gate)
        {
            RetireActiveSlotsWhileLocked("cleanup do formato");
            ReleaseRetiredSlotsWhileLocked();
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Prevents new writers before the native player starts its final teardown.
    /// </summary>
    public void BeginShutdown()
    {
        lock (_gate)
        {
            if (_shutdownRequested)
            {
                return;
            }

            _shutdownRequested = true;
            RetireActiveSlotsWhileLocked("início do shutdown", preserveDiscardForShutdown: true);
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Releases writers that did not receive a display callback. This may only be called after
    /// the owning MediaPlayer has stopped and its native callbacks are known to be quiescent.
    /// Managed read leases remain deferred until their Dispose call.
    /// </summary>
    public void ReleaseRetiredWritesAfterCallbacksStopped()
    {
        lock (_gate)
        {
            ReleaseRetiredSlotsWhileLocked();
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Completes final teardown after MediaPlayer.Dispose has guaranteed that native callbacks
    /// can no longer touch the buffers.
    /// </summary>
    public void CompleteShutdownAfterCallbacksStopped()
    {
        BeginShutdown();
        lock (_gate)
        {
            _disposed = true;
        }

        ReleaseRetiredWritesAfterCallbacksStopped();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownRequested = true;
            RetireActiveSlotsWhileLocked("descarte");
            var busy = _allocatedSlots.Values.Count(slot => slot.State is SlotState.Reading or SlotState.Writing);
            if (busy > 0)
            {
                LogAbnormal($"Descarte adiou {busy} buffer(es) ainda em uso; a memória será liberada pela devolução do lease/callback.");
            }

            Monitor.PulseAll(_gate);
        }
    }

    private void Release(Slot slot)
    {
        lock (_gate)
        {
            if (slot.State != SlotState.Reading)
            {
                return;
            }

            slot.State = SlotState.Free;
            if (slot.Retired || _shutdownRequested || _disposed)
            {
                FreeSlotWhileLocked(slot);
            }

            Monitor.PulseAll(_gate);
        }
    }

    private void RetireActiveSlotsWhileLocked(string reason, bool preserveDiscardForShutdown = false)
    {
        if (_activeSlots.Length == 0 && _activeDiscardSlot is null)
        {
            Width = 0;
            Height = 0;
            Pitch = 0;
            return;
        }

        var busy = 0;
        foreach (var slot in _activeSlots)
        {
            slot.Retired = true;
            if (slot.State is SlotState.Reading or SlotState.Writing)
            {
                busy++;
            }
            else
            {
                FreeSlotWhileLocked(slot);
            }
        }

        if (_activeDiscardSlot is { } discardSlot)
        {
            discardSlot.Retired = true;
            if (preserveDiscardForShutdown)
            {
                _shutdownDiscardSlot = discardSlot;
                if (discardSlot.State == SlotState.Writing)
                {
                    busy++;
                }
            }
            else if (discardSlot.State == SlotState.Writing)
            {
                busy++;
            }
            else
            {
                FreeSlotWhileLocked(discardSlot);
            }
        }

        _activeSlots = [];
        _activeDiscardSlot = null;
        Width = 0;
        Height = 0;
        Pitch = 0;
        if (busy > 0)
        {
            LogAbnormal($"{reason}: {busy} buffer(es) ocupado(s) foram aposentados sem bloquear o teardown.");
        }
    }

    private void ReleaseRetiredSlotsWhileLocked()
    {
        var releasableSlots = _allocatedSlots.Values
            .Where(slot => slot.Retired && slot.State != SlotState.Reading)
            .ToArray();
        var abandonedWriterCount = releasableSlots.Count(slot => slot.State == SlotState.Writing);
        foreach (var slot in releasableSlots)
        {
            slot.DiscardWriterCount = 0;
            slot.State = SlotState.Free;
            FreeSlotWhileLocked(slot);
        }

        if (abandonedWriterCount > 0)
        {
            LogAbnormal($"{abandonedWriterCount} buffer(es) de escrita sem callback final foram liberados após o motor ficar quiescente.");
        }

        if (_shutdownDiscardSlot?.Pointer == nint.Zero)
        {
            _shutdownDiscardSlot = null;
        }
    }

    private Slot? FindWritableSlotWhileLocked()
    {
        foreach (var candidate in _activeSlots)
        {
            if (candidate.State == SlotState.Free)
            {
                return candidate;
            }
        }

        Slot? oldest = null;
        foreach (var candidate in _activeSlots)
        {
            if (candidate.State == SlotState.Ready &&
                (oldest is null || candidate.Sequence < oldest.Sequence))
            {
                oldest = candidate;
            }
        }

        return oldest;
    }

    private nint AcquireDiscardWhileLocked(nint planes, Slot? preferred, string reason)
    {
        var discard = preferred is { Pointer: not 0 } && preferred.Capacity >= _lastBufferBytes
            ? preferred
            : _allocatedSlots.Values.FirstOrDefault(slot =>
                slot.IsDiscard && slot.Pointer != nint.Zero && slot.Capacity >= _lastBufferBytes);

        if (discard is null && _lastBufferBytes > 0)
        {
            try
            {
                discard = AllocateSlots(_lastBufferBytes, 1)[0];
                discard.IsDiscard = true;
                discard.Retired = _shutdownRequested || _activeSlots.Length == 0;
                _allocatedSlots.Add(discard.Pointer, discard);
            }
            catch (Exception exception) when (exception is OverflowException or OutOfMemoryException)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                LogAbnormal($"Não foi possível reservar o buffer de descarte durante {reason}: {exception.Message}");
                return nint.Zero;
            }
        }

        if (discard is null)
        {
            Marshal.WriteIntPtr(planes, nint.Zero);
            LogAbnormal($"O LibVLC solicitou um buffer durante {reason}, mas nenhum formato válido estava configurado.");
            return nint.Zero;
        }

        if (_shutdownRequested)
        {
            discard.Retired = true;
            _shutdownDiscardSlot = discard;
        }
        else if (_activeSlots.Length > 0)
        {
            discard.Retired = false;
            _activeDiscardSlot = discard;
        }

        discard.DiscardWriterCount++;
        discard.State = SlotState.Writing;
        Marshal.WriteIntPtr(planes, discard.Pointer);
        return discard.Pointer;
    }

    private static Slot[] AllocateSlots(nuint bytes, int count)
    {
        var slots = new List<Slot>(count);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var pointer = (nint)NativeMemory.AlignedAlloc(bytes, 32);
                if (pointer == nint.Zero)
                {
                    throw new OutOfMemoryException("A alocação nativa retornou um ponteiro nulo.");
                }

                slots.Add(new Slot(pointer, bytes));
            }

            return [.. slots];
        }
        catch
        {
            foreach (var slot in slots)
            {
                NativeMemory.AlignedFree((void*)slot.Pointer);
            }

            throw;
        }
    }

    private static nuint CalculateBufferSize(uint pitch, uint lines)
    {
        var bytes = checked((ulong)pitch * lines);
        var nativeMaximum = Environment.Is64BitProcess ? ulong.MaxValue : uint.MaxValue;
        var total = checked(bytes * (SlotCount + 1UL));
        if (bytes == 0 || bytes > nativeMaximum || (!Environment.Is64BitProcess && total > int.MaxValue))
        {
            throw new OutOfMemoryException("As dimensões excedem o espaço de endereçamento disponível.");
        }

        return checked((nuint)bytes);
    }

    private void FreeSlotWhileLocked(Slot slot)
    {
        var pointer = slot.Pointer;
        if (pointer == nint.Zero)
        {
            return;
        }

        _allocatedSlots.Remove(pointer);
        slot.Pointer = nint.Zero;
        NativeMemory.AlignedFree((void*)pointer);
    }

    private void LogAbnormal(string message)
    {
        try
        {
            _abnormalStateLogger?.Invoke(message);
        }
        catch
        {
            // Diagnostics must never escape a native video callback or compromise buffer safety.
        }
    }

    internal readonly struct FrameLease : IDisposable
    {
        private readonly LeaseRelease? _release;

        internal FrameLease(FrameBufferRing owner, Slot slot, nint pointer, uint width, uint height, uint pitch)
        {
            _release = new LeaseRelease(owner, slot);
            Pointer = pointer;
            Width = width;
            Height = height;
            Pitch = pitch;
        }

        public nint Pointer { get; }
        public uint Width { get; }
        public uint Height { get; }
        public uint Pitch { get; }

        public void Dispose() => _release?.Dispose();
    }

    /// <summary>
    /// A lease is a value type and can therefore be copied by callers. Keeping the one-shot
    /// release state in a shared reference prevents a second copied Dispose from releasing a
    /// slot that has already been recycled for a newer frame.
    /// </summary>
    private sealed class LeaseRelease(FrameBufferRing owner, Slot slot) : IDisposable
    {
        private FrameBufferRing? _owner = owner;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            currentOwner?.Release(slot);
        }
    }

    internal sealed class Slot(nint pointer, nuint capacity)
    {
        public nint Pointer { get; set; } = pointer;
        public nuint Capacity { get; } = capacity;
        public SlotState State { get; set; }
        public long Sequence { get; set; }
        public bool Retired { get; set; }
        public bool IsDiscard { get; set; }
        public int DiscardWriterCount { get; set; }
    }

    internal enum SlotState
    {
        Free,
        Writing,
        Ready,
        Reading
    }
}
