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
            if (other._type != _type) return false;
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
            using var xLife = GetSequence();
            using var yLife = other.GetSequence();

            var x = xLife.Value;
            var y = yLife.Value;
            if (x.IsSingleSegment && y.IsSingleSegment)
            {
                return x.First.Span.SequenceEqual(y.First.Span);
            }
            throw new NotImplementedException("multi segment equality");
        }

        public override int GetHashCode()
        {
            using var lifetime = GetSequence();
            lifetime.Value // HASH
        }

        private Lifetime<ReadOnlySequence<byte>> GetSequence()
        {
            if (IsAggregate(Type)) ThrowInvalidForType();
            switch (_storage)
            {
                case FrameStorageKind.ArraySegmentByte:
                    var barr = (byte[])_obj0;
                    var start = Overlap(_overlapped64, out var length);
                    return new ReadOnlySequence<byte>(barr, start, length);
                case FrameStorageKind.StringSegment:
                    var s = (string)_obj0;
                    start = Overlap(_overlapped64, out length);
                    return Encode(s.AsSpan(start, length));
                case FrameStorageKind.ArraySegmentChar:
                    var carr = (char[])_obj0;
                    start = Overlap(_overlapped64, out length);
                    return Encode(new ReadOnlySpan<char>(carr, start, length));
                case FrameStorageKind.InlinedBytes:
                    var lifetime = Rent(sizeof(ulong), out var buffer);
                    var tmp = _overlapped64;
                    AsSpan(ref tmp).CopyTo(buffer);
                    return lifetime;
                default:
                    ThrowHelper.FrameStorageKindNotImplemented(_storage);
                    return default;
            }

            static Lifetime<ReadOnlySequence<byte>> Encode(ReadOnlySpan<char> chars)
            {
                
                if (chars.IsEmpty) return default;
                var len = UTF8.GetByteCount(chars);
                var lifetime = Rent(len, out var buffer);
                UTF8.GetBytes(chars, buffer);
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
