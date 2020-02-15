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
                case int i32: typed = i32; break;
                case long i64: typed = i64; break;
                case bool b: typed = b; break;
                // others
                default: return false;
            }
            return Equals(in typed);
        }

        bool IEquatable<RespValue>.Equals(RespValue other) => Equals(in other);
        public bool Equals(in RespValue other)
        {
            if (_type != other._type) return false;
            if (other._storage == _storage && IsInlined)
            {
                // TODO: what about +/-0 ?
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

        public Lifetime<ReadOnlySequence<RespValue>> GetSubItems()
        {
            if (!IsAggregate(_type)) ThrowInvalidForType();
            switch (_storage)
            {
                case FrameStorageKind.ArraySegmentFrame:
                    var x = Overlap(_overlapped64, out var y);
                    return new ReadOnlySequence<RespValue>((RespValue[])_obj0, x, y);
                case FrameStorageKind.MemoryManagerFrame:
                    x = Overlap(_overlapped64, out y);
                    return new ReadOnlySequence<RespValue>(((MemoryManager<RespValue>)_obj0).Memory.Slice(x, y));
                case FrameStorageKind.SequenceSegmentFrame:
                    x = Overlap(_overlapped64, out y);
                    return new ReadOnlySequence<RespValue>(
                        (ReadOnlySequenceSegment<RespValue>)_obj0, x,
                        (ReadOnlySequenceSegment<RespValue>)_obj1, y);
                default:
                    if (IsAggregate(_type) & IsInlined)
                    {
                        var arr = ArrayPool<RespValue>.Shared.Rent(1);
                        arr[0] = new RespValue(_overlapped64, FrameStorageKind.InlinedBytes, _subType, aux: _aux);
                        return new Lifetime<ReadOnlySequence<RespValue>>(
                            new ReadOnlySequence<RespValue>(arr, 0, 1),
                            (_, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state), arr);
                    }
                    ThrowHelper.FrameStorageKindNotImplemented(_storage);
                    return default;
            }
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

        private static readonly ReadOnlySequence<byte> s_MinusOne
            = new ReadOnlySequence<byte>(new byte[] { (byte)'-', (byte)'1' });
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
                    if (_aux == byte.MaxValue) return s_MinusOne;

                    var lifetime = Rent(_aux, out var buffer);
                    var tmp = _overlapped64;
                    AsSpan(ref tmp).Slice(0, _aux).CopyTo(buffer);
                    return lifetime;
                case FrameStorageKind.InlinedInt64:
                    lifetime = Rent(sizeof(ulong), out buffer);
                    var i64 = (long)_overlapped64;
                    if (!Utf8Formatter.TryFormat(i64, buffer, out var len))
                        ThrowHelper.Format();
                    return lifetime.WithValue(lifetime.Value.Slice(0, len));
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
