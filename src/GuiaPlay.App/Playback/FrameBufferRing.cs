using System.Runtime.InteropServices;

namespace GuiaPlay.App.Playback;

internal sealed unsafe class FrameBufferRing : IDisposable
{
    private const int SlotCount = 3;
    private readonly object _gate = new();
    private Slot[] _slots = [];
    private long _sequence;
    private bool _disposed;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint Pitch { get; private set; }

    public void Configure(uint width, uint height, uint pitch, uint lines)
    {
        lock (_gate)
        {
            ReleaseBuffersWhileLocked();
            Width = width;
            Height = height;
            Pitch = pitch;
            var bytes = checked((nuint)(pitch * lines));
            _slots = Enumerable.Range(0, SlotCount)
                .Select(_ => new Slot((nint)NativeMemory.AlignedAlloc(bytes, 32)))
                .ToArray();

            if (_slots.Any(slot => slot.Pointer == nint.Zero))
            {
                ReleaseBuffersWhileLocked();
                throw new OutOfMemoryException("Não foi possível reservar os buffers de vídeo.");
            }
        }
    }

    public nint AcquireWrite(nint planes)
    {
        lock (_gate)
        {
            if (_disposed || _slots.Length == 0)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            var index = FindWritableSlot();

            while (index < 0 && !_disposed)
            {
                Monitor.Wait(_gate, 10);
                index = FindWritableSlot();
            }

            if (index < 0 || _disposed)
            {
                Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            _slots[index].State = SlotState.Writing;
            Marshal.WriteIntPtr(planes, _slots[index].Pointer);
            return new nint(index + 1);
        }
    }

    public void Commit(nint token)
    {
        var index = checked((int)token - 1);
        lock (_gate)
        {
            if (index < 0 || index >= _slots.Length || _slots[index].State != SlotState.Writing)
            {
                return;
            }

            _slots[index].Sequence = ++_sequence;
            _slots[index].State = SlotState.Ready;
        }
    }

    public bool TryAcquireLatest(out FrameLease lease)
    {
        lock (_gate)
        {
            var index = -1;
            var newestSequence = long.MinValue;
            for (var candidate = 0; candidate < _slots.Length; candidate++)
            {
                if (_slots[candidate].State == SlotState.Ready && _slots[candidate].Sequence > newestSequence)
                {
                    newestSequence = _slots[candidate].Sequence;
                    index = candidate;
                }
            }

            if (index < 0)
            {
                lease = default;
                return false;
            }

            for (var candidate = 0; candidate < _slots.Length; candidate++)
            {
                if (candidate != index && _slots[candidate].State == SlotState.Ready)
                {
                    _slots[candidate].State = SlotState.Free;
                }
            }

            _slots[index].State = SlotState.Reading;
            lease = new FrameLease(this, index, _slots[index].Pointer, Width, Height, Pitch);
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            ReleaseBuffersWhileLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ReleaseBuffersWhileLocked();
            Monitor.PulseAll(_gate);
        }
    }

    private void Release(int index)
    {
        lock (_gate)
        {
            if (index >= 0 && index < _slots.Length && _slots[index].State == SlotState.Reading)
            {
                _slots[index].State = SlotState.Free;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void ReleaseBuffersWhileLocked()
    {
        while (HasBusySlot())
        {
            Monitor.Wait(_gate, 10);
        }

        foreach (var slot in _slots)
        {
            if (slot.Pointer != nint.Zero)
            {
                NativeMemory.AlignedFree((void*)slot.Pointer);
            }
        }

        _slots = [];
        Width = 0;
        Height = 0;
        Pitch = 0;
    }

    private bool HasBusySlot()
    {
        foreach (var slot in _slots)
        {
            if (slot.State is SlotState.Reading or SlotState.Writing)
            {
                return true;
            }
        }

        return false;
    }

    private int FindWritableSlot()
    {
        for (var candidate = 0; candidate < _slots.Length; candidate++)
        {
            if (_slots[candidate].State == SlotState.Free)
            {
                return candidate;
            }
        }

        var index = -1;
        var oldestSequence = long.MaxValue;
        for (var candidate = 0; candidate < _slots.Length; candidate++)
        {
            if (_slots[candidate].State == SlotState.Ready && _slots[candidate].Sequence < oldestSequence)
            {
                oldestSequence = _slots[candidate].Sequence;
                index = candidate;
            }
        }

        return index;
    }

    internal readonly struct FrameLease : IDisposable
    {
        private readonly FrameBufferRing? _owner;
        private readonly int _index;

        internal FrameLease(FrameBufferRing owner, int index, nint pointer, uint width, uint height, uint pitch)
        {
            _owner = owner;
            _index = index;
            Pointer = pointer;
            Width = width;
            Height = height;
            Pitch = pitch;
        }

        public nint Pointer { get; }
        public uint Width { get; }
        public uint Height { get; }
        public uint Pitch { get; }

        public void Dispose() => _owner?.Release(_index);
    }

    private sealed class Slot(nint pointer)
    {
        public nint Pointer { get; } = pointer;
        public SlotState State { get; set; }
        public long Sequence { get; set; }
    }

    private enum SlotState
    {
        Free,
        Writing,
        Ready,
        Reading
    }
}
