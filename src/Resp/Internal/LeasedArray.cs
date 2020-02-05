using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Resp.Internal
{
    internal class LeasedArray<T> : IMemoryOwner<T>
    {
        private sealed class EmptyOwner : IMemoryOwner<T>
        {
            public static readonly EmptyOwner Instance = new EmptyOwner();
            private EmptyOwner() { }
            Memory<T> IMemoryOwner<T>.Memory => default;
            void IDisposable.Dispose() { }
        }
        public static IMemoryOwner<T> Rent(int length)
        {
            if (length == 0) return EmptyOwner.Instance;
            if (length < 0) ThrowHelper.ArgumentOutOfRange(nameof(length));

            return new LeasedArray<T>(ArrayPool<T>.Shared.Rent(length), length);
        }

        private LeasedArray(T[] array, int length)
            => Memory = new Memory<T>(array, 0, length);

        public Memory<T> Memory { get; }

        public void Dispose()
        {
            if (MemoryMarshal.TryGetArray<T>(Memory, out var segment))
            {
                Array.Clear(segment.Array, segment.Offset, segment.Count);
                ArrayPool<T>.Shared.Return(segment.Array);
            }
        }
    }
}
