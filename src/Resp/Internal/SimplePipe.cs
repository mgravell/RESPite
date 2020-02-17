using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Resp.Internal
{
    internal sealed class SimplePipe : IBufferWriter<byte>, IDisposable
    {
        public void Clear()
        {
            var node = _startSegment;
            _startSegment = _endSegment = null;
            _startIndex = _endIndex = _writeCapacity = 0;
            node?.RecycleBefore(null);
        }

        public void Dispose() => Clear();

        private Segment _startSegment, _endSegment;
        private int _startIndex, _endIndex, _writeCapacity;

        public ReadOnlySequence<byte> GetBuffer() => _startSegment == null ? default
            : new ReadOnlySequence<byte>(_startSegment, _startIndex, _endSegment, _endIndex);

        public void ConsumeTo(SequencePosition consumed)
        {
            var segment = (Segment)consumed.GetObject();
            var index = consumed.GetInteger();

            if (segment == _endSegment && index == _endIndex)
            {
                // keep the last page; burn anything else
                _startSegment.RecycleBefore(segment);
                _startSegment = _endSegment;
                _startIndex = _endIndex = 0;
            }
            else
            {
                // discard any pages that we no longer need
                _startSegment.RecycleBefore(segment);
                _startSegment = segment;
                _startIndex = index;
            }
        }

        
        void IBufferWriter<byte>.Advance(int count)
        {
            static void OutOfRange(int count, int capacity)
               => ThrowHelper.ArgumentOutOfRange(nameof(count), $"Advance called with count of {count}; write capacity is {capacity}");

            if (count < 0 | count > _writeCapacity) OutOfRange(count, _writeCapacity);
            _endIndex += count;
            _writeCapacity = 0;
        }

        private Memory<byte> GetWriteBuffer(int sizeHint)
        {
            if (_endSegment != null)
            {
                sizeHint = Math.Max(1, Math.Min(sizeHint, _maxBlockSize)); // apply a reasonable upper bound
                var memory = MemoryMarshal.AsMemory(_endSegment.Memory.Slice(_endIndex));
                var capacity = memory.Length;
                if (capacity >= sizeHint)
                {
                    _writeCapacity = capacity;
                    return memory;
                }
            }
            return AppendNewBuffer(sizeHint);
        }

        private readonly int _minBlockSize, _maxBlockSize;
        public SimplePipe(int minBlockSize = 4 * 1024, int maxBlockSize = 64 * 1024)
        {
            if (minBlockSize <= 0) ThrowHelper.ArgumentOutOfRange(nameof(minBlockSize));
            if (maxBlockSize < minBlockSize) ThrowHelper.ArgumentOutOfRange(nameof(maxBlockSize));
            _minBlockSize = minBlockSize;
            _maxBlockSize = maxBlockSize;
        }

        private Memory<byte> AppendNewBuffer(int sizeHint)
        {
            sizeHint = Math.Max(sizeHint, _minBlockSize); // request at least a decent sized buffer
            var oldFinal = _endSegment;
            if (oldFinal != null)
            {
                if (_endIndex == 0)
                {
                    // we need to discard the last buffer entirely
                    if (_startSegment == _endSegment)
                    {
                        // discard everything
                        _startSegment = _endSegment = null;
                    }
                    else
                    {
                        // just the last buffer
                        _endSegment = _startSegment.FindPrevious(oldFinal);
                        _endSegment.ClearNext();
                    }
                    oldFinal.Recycle();
                }
                else
                {
                    // we need to prune the last buffer to respect the used portion
                    oldFinal.Trim(_endIndex);
                }
            }

            Memory<byte> buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            _endSegment = Segment.Create(_endSegment, buffer);
            _endIndex = 0;
            if (_startSegment == null)
            {
                _startSegment = _endSegment;
                _startIndex = 0;
            }
            _writeCapacity = buffer.Length;
            return buffer;
        }

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => GetWriteBuffer(sizeHint);
        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => GetWriteBuffer(sizeHint).Span;

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            internal void Trim(int length)
                => Memory = Memory.Slice(0, length);

            internal void ClearNext() => Next = null;

            internal Segment FindPrevious(Segment find)
            {
                ReadOnlySequenceSegment<byte> node = this;
                while (node != null)
                {
                    var next = node.Next;
                    if (next == find) return (Segment)node;
                    node = next;
                }
                return null;
            }

            internal static Segment Create(Segment previous, Memory<byte> buffer)
            {
                var obj = s_spare ?? new Segment();
                s_spare = null;
                return obj.Init(previous, buffer);
            }

            [ThreadStatic]
            private static Segment s_spare;
            public void Recycle()
            {
                if (MemoryMarshal.TryGetArray(Memory, out var segment))
                {
                    ArrayPool<byte>.Shared.Return(segment.Array);
                }
                s_spare = Init(null, null);
            }

            private Segment() {  }
            private Segment Init(Segment previous, Memory<byte> buffer)
            {
                Memory = buffer;
                Next = null;
                RunningIndex = 0;
                if (previous != null)
                {
                    RunningIndex = previous.RunningIndex + previous.Memory.Length;
                    previous.Next = this;
                }
                return this;
            }

            internal void RecycleBefore(Segment retain)
            {
                var node = this;
                while (node != null && node != retain)
                {
                    var next = (Segment)node.Next;
                    node.Recycle();
                    node = next;
                }
            }
        }
    }
}
