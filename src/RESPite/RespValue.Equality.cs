using Respite.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace Respite
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
            if (_state.Equals(in other._state)
                & _obj0 == other._obj0
                & _obj1 == other._obj1)
                return true; // clearly identical
            if (Type != other.Type)
                return false; // clearly different

            // check for null-wise equality
            if (_state.Storage == StorageKind.Null)
            {
                return other._state.Storage == StorageKind.Null;
            }
            else if (other._state.Storage == StorageKind.Null)
            {
                return false;
            }
            return GetAggregateArity(Type) == 0
                ? CompareBytes(in other)
                : CompareAggregate(in other);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool CompareAggregate(in RespValue other)
        {
            if (IsUnitAggregate(out var x))
            {
                return other.IsUnitAggregate(out var y) && x.Equals(y);
            }
            else if (other.IsUnitAggregate(out _)) return false;

            return SequenceEqual(GetSubValues(), other.GetSubValues());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
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
            switch (_state.Storage)
            {
                case StorageKind.Null:
                case StorageKind.Empty:
                    bytes = default;
                    return true;
                //case StorageKind.Utf8StringSegment:
                    // TODO
                case StorageKind.ArraySegmentByte:
                    bytes = new ReadOnlySequence<byte>((byte[])_obj0,
                        _state.StartOffset, _state.Length);
                    return true;
                case StorageKind.MemoryManagerByte:
                    bytes = new ReadOnlySequence<byte>(((MemoryManager<byte>)_obj0).Memory
                        .Slice(_state.StartOffset, _state.Length));
                    return true;
                case StorageKind.SequenceSegmentByte:
                    bytes = new ReadOnlySequence<byte>(
                        (ReadOnlySequenceSegment<byte>)_obj0, _state.StartOffset,
                        (ReadOnlySequenceSegment<byte>)_obj1, _state.EndOffset);
                    return true;
                default:
                    bytes = default;
                    return false;
            }
        }

        private bool TryGetChars(out ReadOnlySequence<char> chars)
        {
            switch (_state.Storage)
            {
                case StorageKind.Null:
                case StorageKind.Empty:
                    chars = default;
                    return true;
                case StorageKind.StringSegment:
                    chars = new ReadOnlySequence<char>(((string)_obj0).AsMemory(
                        _state.StartOffset, _state.Length));
                    return true;
                case StorageKind.ArraySegmentChar:
                    chars = new ReadOnlySequence<char>((char[])_obj0,
                        _state.StartOffset, _state.Length);
                    return true;
                case StorageKind.MemoryManagerChar:
                    chars = new ReadOnlySequence<char>(((MemoryManager<char>)_obj0).Memory
                        .Slice(_state.StartOffset, _state.Length));
                    return true;
                case StorageKind.SequenceSegmentChar:
                    chars = new ReadOnlySequence<char>(
                        (ReadOnlySequenceSegment<char>)_obj0, _state.StartOffset,
                        (ReadOnlySequenceSegment<char>)_obj1, _state.EndOffset);
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
            if (GetAggregateArity(Type) != 0) ThrowInvalidForType();

            if (TryGetBytes(out var bytes))
            {
                return bytes;
            }
            if (TryGetChars(out var chars))
            {
                return Encode(chars);
            }
            switch (_state.Storage)
            {
                case StorageKind.InlinedBytes:
                    var lifetime = Rent(_state.PayloadLength, out var buffer);
                    _state.AsSpan().CopyTo(buffer);
                    return lifetime;
                case StorageKind.InlinedInt64:
                    lifetime = Rent(20, out buffer);
                    if (!Utf8Formatter.TryFormat(_state.Int64, buffer, out var len))
                        ThrowHelper.Format();
                    return lifetime.WithValue(lifetime.Value.Slice(0, len));
                case StorageKind.InlinedUInt32:
                    lifetime = Rent(20, out buffer);
                    if (!Utf8Formatter.TryFormat(_state.UInt32, buffer, out len))
                        ThrowHelper.Format();
                    return lifetime.WithValue(lifetime.Value.Slice(0, len));
                case StorageKind.InlinedDouble:
                    lifetime = Rent(64, out buffer);
                    if (!Utf8Formatter.TryFormat(_state.Double, buffer, out len))
                        ThrowHelper.Format();
                    return lifetime.WithValue(lifetime.Value.Slice(0, len));
                default:
                    ThrowHelper.StorageKindNotImplemented(_state.Storage);
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
    }
}
