using Respite.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Numerics;

namespace Respite
{
    partial struct RespValue
    {
        public static RespValue True => s_True;
        public static RespValue False => s_False;

        private static readonly RespValue
            s_True = Create(RespType.Boolean, "t"),
            s_False = Create(RespType.Boolean, "f"),
            s_PositiveInfinity = Create(RespType.Double, "+inf"),
            s_NegativeInfinity = Create(RespType.Double, "-inf");

        public static implicit operator RespValue(string value)
            => Create(RespType.BlobString, value);
        public static implicit operator RespValue(byte[] value)
            => Create(RespType.BlobString, new ReadOnlySequence<byte>(value));
        public static implicit operator RespValue(ArraySegment<byte> value)
            => Create(RespType.BlobString, new ReadOnlySequence<byte>(value.Array, value.Offset, value.Count));

        public static implicit operator RespValue(bool value) => value ? s_True : s_False;

        public static implicit operator RespValue(int value) => Create(RespType.Number, (long)value);
        public static implicit operator RespValue(long value) => Create(RespType.Number, value);

        public static implicit operator RespValue(double value)
        {
            if (double.IsPositiveInfinity(value)) return s_PositiveInfinity;
            if (double.IsNegativeInfinity(value)) return s_NegativeInfinity;
            return Create(RespType.Double, value);
        }

        public static explicit operator string(in RespValue value) => value.ToString();

        public bool ToBoolean()
        {
            if (_state.Storage == StorageKind.InlinedBytes)
            {
                if (_state.PayloadLength == 1)
                {
                    switch (_state.Byte)
                    {
                        case (byte)'t': return true;
                        case (byte)'f': return false;
                    }
                }
                ThrowHelper.Format();
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return default;
        }

        public long ToInt64()
        {
            switch (_state.Storage)
            {
                case StorageKind.InlinedBytes:
                    if (_state.TryParseInlinedBytes(out long i64))
                        return i64;
                    if (_state.TryParseInlinedBytes(out double d64))
                        return (long)d64;
                    ThrowHelper.Format();
                    return default;
                case StorageKind.InlinedDouble:
                    return (long)_state.Double;
                case StorageKind.InlinedInt64:
                    return _state.Int64;
                case StorageKind.ArraySegmentByte:
                    var span = new ReadOnlySpan<byte>((byte[])_obj0!, _state.StartOffset, _state.Length);
                    if (Utf8Parser.TryParse(span, out i64, out int bytes) && bytes == span.Length)
                        return i64;
                    ThrowHelper.Format();
                    return default;
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return default;
        }

        public double ToDouble()
        {
            switch (_state.Storage)
            {
                case StorageKind.InlinedBytes:
                    if (_state.TryParseInlinedBytes(out double d64))
                        return d64;
                    ThrowHelper.Format();
                    return default;
                case StorageKind.InlinedDouble:
                    return _state.Double;
                case StorageKind.InlinedInt64:
                    return _state.Int64;
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return default;
        }
    }
}
