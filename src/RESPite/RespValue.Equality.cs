using Respite.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
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
            var x = this.SubItems;
            var y = other.SubItems;
            if (x.Count != y.Count) return false;
            if (x.IsEmpty) return true;

            if (x.TryGetSingle(out var xv) && y.TryGetSingle(out var yv))
                return xv.Equals(yv);

            if (x.TryGetSingleSpan(out var xs) && y.TryGetSingleSpan(out var ys))
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    if (!xs[i].Equals(ys[i])) return false;
                }
                return true;
            }

            var ex = x.GetEnumerator();
            var ey = y.GetEnumerator();
            while (ex.MoveNext())
            {
                if (!ey.MoveNext()) return false;

                if (!ex.Current.Equals(ey.Current)) return false;
            }
            if (ey.MoveNext()) return false;
            return true;
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

        public long GetByteCount()
        {
            switch (_state.Storage)
            {
                case StorageKind.Uninitialized:
                case StorageKind.Null:
                case StorageKind.Empty:
                case StorageKind.ArraySegmentRespValue:
                case StorageKind.MemoryManagerRespValue:
                case StorageKind.SequenceSegmentRespValue:
                    return 0;
                case StorageKind.Utf8StringSegment:
                case StorageKind.MemoryManagerByte:
                case StorageKind.ArraySegmentByte:
                    return _state.Length;
                case StorageKind.SequenceSegmentByte:
                    if (!TryGetBytes(out var bytes)) break;
                    return (int)bytes.Length;
                case StorageKind.StringSegment:
                case StorageKind.ArraySegmentChar:
                case StorageKind.MemoryManagerChar:
                case StorageKind.SequenceSegmentChar:
                    if (!TryGetChars(out var chars)) break;
                    return CountUtf8(chars);
                case StorageKind.InlinedBytes:
                    return _state.PayloadLength;
                case StorageKind.InlinedDouble:
                case StorageKind.InlinedInt64:
                case StorageKind.InlinedUInt32:
                    Span<byte> tmp = stackalloc byte[32];
                    return WriteInlineValue(tmp);
                    //case StorageKind.InlinedDouble:
                    //    // this needs to be large because of RESP3
                    //    // disallowing exponential format
                    //    // see: https://github.com/antirez/RESP3/issues/37
                    //    tmp = stackalloc byte[320];
                    //    return WriteInlineValue(tmp);
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return 0;
        }

        public int CopyTo(Span<byte> destination)
        {
            switch (_state.Storage)
            {
                case StorageKind.Uninitialized:
                case StorageKind.Null:
                case StorageKind.Empty:
                case StorageKind.ArraySegmentRespValue:
                case StorageKind.MemoryManagerRespValue:
                case StorageKind.SequenceSegmentRespValue:
                    return 0;
                case StorageKind.Utf8StringSegment:
                case StorageKind.MemoryManagerByte:
                case StorageKind.ArraySegmentByte:
                case StorageKind.SequenceSegmentByte:
                    if (!TryGetBytes(out var bytes)) break;
                    bytes.CopyTo(destination);
                    return (int)bytes.Length;
                case StorageKind.StringSegment:
                case StorageKind.ArraySegmentChar:
                case StorageKind.MemoryManagerChar:
                case StorageKind.SequenceSegmentChar:
                    if (!TryGetChars(out var chars)) break;
                    return EncodeUtf8(chars, destination);
                case StorageKind.InlinedBytes:
                case StorageKind.InlinedDouble:
                case StorageKind.InlinedInt64:
                case StorageKind.InlinedUInt32:
                    return WriteInlineValue(destination);
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return 0;
        }

        private int WriteInlineValue(Span<byte> destination)
        {
            switch (_state.Storage)
            {
                case StorageKind.InlinedDouble:
                    return RespWriter.Write(_state.Double, destination);
                case StorageKind.InlinedInt64:
                    return RespWriter.Write(_state.Int64, destination);
                case StorageKind.InlinedUInt32:
                    return RespWriter.Write(_state.UInt32, destination);
                case StorageKind.InlinedBytes:
                    _state.AsSpan().CopyTo(destination);
                    return _state.PayloadLength;
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return 0;
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
                    bytes = new ReadOnlySequence<byte>((byte[])_obj0!,
                        _state.StartOffset, _state.Length);
                    return true;
                case StorageKind.MemoryManagerByte:
                    bytes = new ReadOnlySequence<byte>(((MemoryManager<byte>)_obj0!).Memory
                        .Slice(_state.StartOffset, _state.Length));
                    return true;
                case StorageKind.SequenceSegmentByte:
                    bytes = new ReadOnlySequence<byte>(
                        (ReadOnlySequenceSegment<byte>)_obj0!, _state.StartOffset,
                        (ReadOnlySequenceSegment<byte>)_obj1!, _state.EndOffset);
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
                    chars = new ReadOnlySequence<char>(((string)_obj0!).AsMemory(
                        _state.StartOffset, _state.Length));
                    return true;
                case StorageKind.ArraySegmentChar:
                    chars = new ReadOnlySequence<char>((char[])_obj0!,
                        _state.StartOffset, _state.Length);
                    return true;
                case StorageKind.MemoryManagerChar:
                    chars = new ReadOnlySequence<char>(((MemoryManager<char>)_obj0!).Memory
                        .Slice(_state.StartOffset, _state.Length));
                    return true;
                case StorageKind.SequenceSegmentChar:
                    chars = new ReadOnlySequence<char>(
                        (ReadOnlySequenceSegment<char>)_obj0!, _state.StartOffset,
                        (ReadOnlySequenceSegment<char>)_obj1!, _state.EndOffset);
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
            var expected = checked((int)GetByteCount());
            var lifetime = Lifetime.RentSequence(expected, out var buffer);
            var actual = CopyTo(buffer);
            Debug.Assert(actual == expected, "length calculation mismatch");
            return lifetime;
        }

        void ThrowInvalidForType()
            => throw new InvalidOperationException($"Invalid operation for {Type}");
    }
}
