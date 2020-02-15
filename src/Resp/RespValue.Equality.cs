using Resp.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace Resp
{
    partial struct RespValue : IEquatable<RespValue>
    {
        public override bool Equals(object other)
        {
            RespValue typed;
            switch (other)
            {
                case RespValue rv: typed = rv; break;
                case string s: typed = s; break;
                // others
                default: return false;
            }
            return Equals(in typed);
        }

        bool IEquatable<RespValue>.Equals(RespValue other) => Equals(in other);
        public bool Equals(in RespValue other)
        {
            if (other._storage == _storage && IsInlined)
            {
                return other._overlapped64 == _overlapped64
                    && other._aux == _aux;
            }
            return CompareBytes(in other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CompareBytes(in RespValue other)
        {
            if (TryGetBytes(out var xb) && other.TryGetBytes(out var yb))
            {
                return SequenceEqual(xb, yb);
            }
            if (TryGetChars(out var xc) && other.TryGetChars(out var yc))
            {
                return SequenceEqual(xc, yc);
            }

            using var xLife = GetSequence();
            using var yLife = other.GetSequence();
            return SequenceEqual(xLife.Value, yLife.Value);
        }

        static bool SequenceEqual<T>(ReadOnlySequence<T> x, ReadOnlySequence<T> y) where T : IEquatable<T>
        {
            if (x.IsSingleSegment & y.IsSingleSegment) return x.FirstSpan.SequenceEqual(y.FirstSpan);

            if (x.Length != x.Length) return false;
            while (!x.IsEmpty)
            {
                var xs = x.FirstSpan;
                var ys = y.FirstSpan;
                var min = Math.Min(xs.Length, ys.Length);
                if (min == 0) ThrowHelper.NotImplemented();
                if (!xs.Slice(0, min).SequenceEqual(ys.Slice(0, min))) return false;
                x = x.Slice(min);
                y = y.Slice(min);
            }
            return true;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        private bool TryGetBytes(out ReadOnlySequence<byte> bytes)
        { 
            switch (_storage)
            {
                //case FrameStorageKind.Utf8StringSegment:
                    // TODO
                case FrameStorageKind.ArraySegmentByte:
                    var x = Overlap(_overlapped64, out var y);
                    bytes = new ReadOnlySequence<byte>((byte[])_obj0, x, y);
                    return true;
                case FrameStorageKind.MemoryManagerByte:
                    x = Overlap(_overlapped64, out y);
                    bytes = new ReadOnlySequence<byte>(((MemoryManager<byte>)_obj0).Memory.Slice(x, y));
                    return true;
                case FrameStorageKind.SequenceSegmentByte:
                    x = Overlap(_overlapped64, out y);
                    bytes = new ReadOnlySequence<byte>(
                        (ReadOnlySequenceSegment<byte>)_obj0, x,
                        (ReadOnlySequenceSegment<byte>)_obj1, y);
                    return true;
                default:
                    bytes = default;
                    return false;
            }
        }

        private bool TryGetChars(out ReadOnlySequence<char> chars)
        {
            switch (_storage)
            {
                case FrameStorageKind.StringSegment:
                    var x = Overlap(_overlapped64, out var y);
                    chars = new ReadOnlySequence<char>(((string)_obj0).AsMemory(x, y));
                    return true;
                case FrameStorageKind.ArraySegmentChar:
                    x = Overlap(_overlapped64, out y);
                    chars = new ReadOnlySequence<char>((char[])_obj0, x, y);
                    return true;
                case FrameStorageKind.MemoryManagerChar:
                    x = Overlap(_overlapped64, out y);
                    chars = new ReadOnlySequence<char>(((MemoryManager<char>)_obj0).Memory.Slice(x, y));
                    return true;
                case FrameStorageKind.SequenceSegmentChar:
                    x = Overlap(_overlapped64, out y);
                    chars = new ReadOnlySequence<char>(
                        (ReadOnlySequenceSegment<char>)_obj0, x,
                        (ReadOnlySequenceSegment<char>)_obj1, y);
                    return true;
                default:
                    chars = default;
                    return false;
            }
        }

        private Lifetime<ReadOnlySequence<byte>> GetSequence()
        {
            if (IsAggregate(Type)) ThrowInvalidForType();

            if (TryGetBytes(out var bytes))
            {
                return bytes;
            }
            if (TryGetChars(out var chars))
            {
                return Encode(chars);
            }
            switch (_storage)
            {
                case FrameStorageKind.InlinedBytes:
                    var lifetime = Rent(sizeof(ulong), out var buffer);
                    var tmp = _overlapped64;
                    AsSpan(ref tmp).CopyTo(buffer);
                    return lifetime;
                default:
                    ThrowHelper.FrameStorageKindNotImplemented(_storage);
                    return default;
            }

            static Lifetime<ReadOnlySequence<byte>> Encode(in ReadOnlySequence<char> chars)
            {
                if (chars.IsEmpty) return default;
                int len = 0;
                foreach(var block in chars)
                {
                    len += UTF8.GetByteCount(block.Span);
                }
                var lifetime = Rent(len, out var buffer);
                foreach (var block in chars)
                {
                    buffer = buffer.Slice(UTF8.GetBytes(block.Span, buffer));
                }   
                return lifetime;
            }
        }

        static Lifetime<ReadOnlySequence<byte>> Rent(int length, out Span<byte> buffer)
        {
            var arr = ArrayPool<byte>.Shared.Rent(length);
            buffer = new Span<byte>(arr, 0, length);
            var seq = new ReadOnlySequence<byte>(arr, 0, length);
            return new Lifetime<ReadOnlySequence<byte>>(seq, (_, state) => ArrayPool<byte>.Shared.Return((byte[])state), arr);
        }

        void ThrowInvalidForType()
            => throw new InvalidOperationException($"Invalid operation for {Type}");

        private bool IsInlined
        {
            get
            {
                switch (_storage)
                {
                    case FrameStorageKind.InlinedBytes:
                    case FrameStorageKind.InlinedDouble:
                    case FrameStorageKind.InlinedInt64:
                    case FrameStorageKind.InlinedUInt32:
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
