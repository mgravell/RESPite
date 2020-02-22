using Respite.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Respite
{

    public enum RespType : byte
    {
        // \0$+-:_,#!=(*%~|>
        Unknown = 0,
        BlobString = (byte)'$',
        SimpleString = (byte)'+',
        SimpleError = (byte)'-',
        Number = (byte)':',
        Null = (byte)'_',
        Double = (byte)',',
        Boolean = (byte)'#',
        BlobError = (byte)'!',
        VerbatimString = (byte)'=',
        BigNumber = (byte)'(',
        Array = (byte)'*',
        Map = (byte)'%',
        Set = (byte)'~',
        Attribute = (byte)'|',
        Push = (byte)'>',
    }

    internal enum StorageKind : byte
    {
        Uninitialized,
        Null,
        Empty,
        InlinedBytes, // overlapped = payload, aux = length
        InlinedUInt32, // overlapped = payload
        InlinedInt64, // overlapped = payload
        InlinedDouble, // overlapped = payload
        ArraySegmentByte, // overlapped = offset/length, obj0 = array
        ArraySegmentChar, // overlapped = offset/length, obj0 = array
        StringSegment, // overlapped = offset/length, obj0 = value
        Utf8StringSegment, // overlapped = offset/length, obj0 = value
        MemoryManagerByte, // overlapped = offset/length, obj0 = manager
        MemoryManagerChar, // overlapped = offset/length, obj0 = manager
        SequenceSegmentByte, // overlapped = offset/length, obj0 = memory owner
        SequenceSegmentChar, // overlapped = start index/end index, obj0 = start segment, obj1 = end segment

        ArraySegmentRespValue, // overlapped = offset/length, obj0 = array
        MemoryManagerRespValue, // overlapped = offset/length, obj0 = manager
        SequenceSegmentRespValue, // overlapped = start index/end index, obj0 = start segment, obj1 = end segment
    }

    public readonly partial struct RespValue
    {
        private readonly State _state;
        private readonly object? _obj0, _obj1;

        public static readonly RespValue Ping = Command("PING");

        public RespType Type => _state.Type;

        public RespValueItems SubItems => new RespValueItems(in this);

        private static Encoding ASCII => Encoding.ASCII;
        private static Encoding UTF8 => Encoding.UTF8;

        public override string ToString()
        {
            if (TryGetChars(out var chars))
            {
                if (chars.IsSingleSegment)
                {
                    if (MemoryMarshal.TryGetString(chars.First, out var s,
                        out var start, out var length))
                    {
                        return s.Substring(start, length);
                    }
                    return new string(chars.First.Span);
                }
            }
            if (TryGetBytes(out var bytes))
            {
                if (bytes.IsSingleSegment)
                    return UTF8.GetString(bytes.First.Span);
            }

            switch (_state.Storage)
            {
                case StorageKind.Empty:
                case StorageKind.Null:
                    return "";
                case StorageKind.InlinedBytes:
                    return UTF8.GetString(_state.AsSpan());
                case StorageKind.InlinedInt64:
                    return _state.Int64.ToString(NumberFormatInfo.InvariantInfo);
                case StorageKind.InlinedUInt32:
                    return _state.UInt32.ToString(NumberFormatInfo.InvariantInfo);
                case StorageKind.InlinedDouble:
                    var r64 = _state.Double;
                    if (double.IsInfinity(r64))
                    {
                        if (double.IsPositiveInfinity(r64)) return "+inf";
                        if (double.IsNegativeInfinity(r64)) return "-inf";
                    }
                    return r64.ToString("G17", NumberFormatInfo.InvariantInfo);
                default:
                    ThrowHelper.StorageKindNotImplemented(_state.Storage);
                    return default!;
            }
        }

        private RespValue(State state, object? obj0 = null, object? obj1 = null)
        {
            _state = state;
            _obj0 = obj0;
            _obj1 = obj1;
        }

        public static Lifetime<Memory<RespValue>> Lease(int length)
        {
            if (length < 0) ThrowHelper.ArgumentOutOfRange(nameof(length));
            if (length == 0) return default;
            var arr = ArrayPool<RespValue>.Shared.Rent(length);

            var memory = new Memory<RespValue>(arr, 0, length);
            memory.Span.Clear();
            return new Lifetime<Memory<RespValue>>(memory, (_, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state!), arr);
        }

        private static RespValue Command(string command)
        {
            var len = ASCII.GetByteCount(command);
            if (len <= State.InlineSize)
            {
                var state = new State((byte)len, RespType.Array, RespType.BlobString);
                ASCII.GetBytes(command.AsSpan(), state.AsWritableSpan());
                return new RespValue(state);
            }
            else
            {
                var arr = ASCII.GetBytes(command); // pre-encode
                return new RespValue(new State(RespType.Array,
                    StorageKind.ArraySegmentByte, 0, len, RespType.BlobString), arr);
            }
        }

        private static RespValue Create(RespType type, ReadOnlySequence<byte> payload)
        {
            var len = payload.Length;
            if (len == 0)
            {
                return new RespValue(new State(type,
                    type == RespType.Null ? StorageKind.Null : StorageKind.Empty, 0, 0));
            }
            else if (len <= State.InlineSize)
            {
                var state = new State((byte)len, type);
                if (len != 0) payload.CopyTo(state.AsWritableSpan());
                return new RespValue(state);
            }
            else if (payload.IsSingleSegment)
            {
                var memory = payload.First;
                if (MemoryMarshal.TryGetArray(memory, out var segment))
                {
                    return new RespValue(
                        new State(type, StorageKind.ArraySegmentByte, segment.Offset, segment.Count),
                        segment.Array);
                }
                else if (MemoryMarshal.TryGetMemoryManager(memory, out MemoryManager<byte> manager, out var start, out var length))
                {
                    return new RespValue(
                        new State(type, StorageKind.MemoryManagerByte, start, length),
                        manager);
                }
            }
            SequencePosition seqStart = payload.Start, seqEnd = payload.End;
            if (seqStart.GetObject() is ReadOnlySequenceSegment<byte> segStart
                && seqEnd.GetObject() is ReadOnlySequenceSegment<byte> segEnd)
            {
                return new RespValue(
                    new State(type, StorageKind.SequenceSegmentByte,
                    seqStart.GetInteger(), seqEnd.GetInteger()), segStart, segEnd);
            }
            ThrowHelper.UnknownSequenceVariety();
            return default;
        }

        public long Write(IBufferWriter<byte> output, RespVersion version)
        {
            var writer = new RespWriter(version, output);
            Write(ref writer);
            return writer.Complete();
        }
        private void Write(ref RespWriter writer)
        {
            if (GetAggregateArity(Type) != 0)
            {
                WriteAggregate(ref writer);
            }
            else
            {
                WriteValue(ref writer, _state.Type);
            }
        }

        private void WriteAggregate(ref RespWriter writer)
        {
            switch (_state.Storage)
            {
                case StorageKind.Null:
                    WriteNull(ref writer, _state.Type);
                    break;
                case StorageKind.Empty:
                    WriteEmpty(ref writer, _state.Type);
                    break;
                case StorageKind.InlinedBytes when IsBlob(_state.SubType):
                    WriteUnitAggregateInlinedBlob(ref writer);
                    break;
                default:
                    var type = Type;
                    if (writer.Version < RespVersion.RESP3) type = writer.Downgrade(type);
                    if (IsUnitAggregate(out var value))
                    {
                        WriteUnitAggregate(ref writer, type, in value);
                    }
                    else
                    {
                        WriteAggregate(ref writer, type, GetSubValues());
                    }
                    break;
            }


            //    case StorageKind.ArraySegmentRespValue:
            //        Write(output, _state.Type, new ReadOnlySpan<RespValue>((RespValue[])_obj0,
            //            _state.StartOffset, _state.Length));
            //        break;
            //    case StorageKind.MemoryManagerRespValue:
            //        Write(output, _state.Type, ((MemoryManager<RespValue>)_obj0).Memory.Span
            //            .Slice(_state.StartOffset, _state.Length));
            //        break;
            //    case StorageKind.SequenceSegmentRespValue:
            //        ThrowHelper.StorageKindNotImplemented(_state.Storage);
            //        break;
            //    default:
            //        WriteUnitAggregate(output);
            //        break;
            //}
        }

        internal int GetSubValueCount()
        {
            switch (_state.Storage)
            {
                case StorageKind.MemoryManagerRespValue:
                case StorageKind.ArraySegmentRespValue:
                    return _state.Length;
                case StorageKind.SequenceSegmentRespValue:
                    return checked((int)new ReadOnlySequence<RespValue>(
                        (ReadOnlySequenceSegment<RespValue>)_obj0!,
                        _state.StartOffset,
                        (ReadOnlySequenceSegment<RespValue>)_obj1!,
                        _state.EndOffset).Length);
                case StorageKind.InlinedDouble:
                case StorageKind.InlinedInt64:
                case StorageKind.InlinedUInt32:
                case StorageKind.InlinedBytes:
                    return _state.SubType == RespType.Unknown ? 0 : 1;
                default:
                    return 0;
            }
        }
        internal ReadOnlySequence<RespValue> GetSubValues()
        {
            switch (_state.Storage)
            {
                case StorageKind.ArraySegmentRespValue:
                    return new ReadOnlySequence<RespValue>((RespValue[])_obj0!,
                        _state.StartOffset, _state.Length);
                case StorageKind.MemoryManagerRespValue:
                    return new ReadOnlySequence<RespValue>(
                        ((MemoryManager<RespValue>)_obj0!).Memory
                            .Slice(_state.StartOffset, _state.Length));
                case StorageKind.SequenceSegmentRespValue:
                    return new ReadOnlySequence<RespValue>(
                        (ReadOnlySequenceSegment<RespValue>)_obj0!,
                        _state.StartOffset,
                        (ReadOnlySequenceSegment<RespValue>)_obj1!,
                        _state.EndOffset);
                case StorageKind.InlinedDouble:
                case StorageKind.InlinedInt64:
                case StorageKind.InlinedUInt32:
                case StorageKind.InlinedBytes:
                    if (_state.SubType != RespType.Unknown)
                        ThrowHelper.Invalid("This aggregate must be accessed via " + nameof(IsUnitAggregate));
                    return default;
                default:
                    return default;
            }
        }
        public bool IsUnitAggregate(out RespValue value)
        {
            if (GetAggregateArity(_state.Type) == 1)
            {
                switch (_state.Storage)
                {
                    case StorageKind.Empty:
                    case StorageKind.Null:
                        break;
                    case StorageKind.ArraySegmentRespValue:
                        if (_state.Length == 1)
                        {
                            value = ((RespValue[])_obj0!)[_state.StartOffset];
                            return true;
                        }
                        break;
                    case StorageKind.MemoryManagerRespValue:
                        if (_state.Length == 1)
                        {
                            value = ((MemoryManager<RespValue>)_obj0!).GetSpan()[_state.StartOffset];
                            return true;
                        }
                        break;
                    case StorageKind.SequenceSegmentRespValue:
                        var seq = new ReadOnlySequence<RespValue>(
                            (ReadOnlySequenceSegment<RespValue>)_obj0!, _state.StartOffset,
                            (ReadOnlySequenceSegment<RespValue>)_obj1!, _state.EndOffset);
                        if (seq.Length == 1)
                        {
                            value = seq.FirstSpan[0];
                            return true;
                        }
                        break;
                    default:
                        if (_state.IsInlined && _state.SubType != RespType.Unknown)
                        {
                            value = new RespValue(_state.Unwrap());
                            return true;
                        }
                        break;
                }
            }
            value = default;
            return false;
        }

        private static void WriteAggregate(ref RespWriter writer, RespType aggregateType, ReadOnlySequence<RespValue> values)
        {
            // {type}{count}\r\n
            // {payload0}\r\n
            // {payload1}\r\n
            // {payload...}\r\n
            writer.Write(aggregateType);
            writer.Write((ulong)(values.Length / GetAggregateArity(aggregateType)));
            writer.WriteLine();
            if (values.IsSingleSegment)
            {
                foreach (ref readonly RespValue value in values.FirstSpan)
                    value.Write(ref writer);
            }
            else
            {
                foreach (var segment in values)
                {
                    foreach (ref readonly RespValue value in segment.Span)
                        value.Write(ref writer);
                }
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //private static bool EqualsShortAlphaIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> lowerCaseValue)
        //{
        //    return value.Length switch
        //    {

        //        // to lower-casify; note that for non-alpha, this is an invalid thing, so
        //        // this should *only* be used when the comparison value is known to be alpha

        //        // OK
        //        2 => (BitConverter.ToInt16(value) | 0x2020) == BitConverter.ToInt16(lowerCaseValue),
        //        // PONG
        //        4 => (BitConverter.ToInt32(value) | 0x20202020) == BitConverter.ToInt32(lowerCaseValue),
        //        _ => EqualsSlow(value, lowerCaseValue),
        //    };

        //    static bool EqualsSlow(ReadOnlySpan<byte> value, ReadOnlySpan<byte> lowerCaseValue)
        //    {
        //        // compare in 8-byte chunks as var as possible; could also look at SIMD, but...
        //        // how large values are we expecting, really?
        //        var value64 = MemoryMarshal.Cast<byte, long>(value);
        //        var lowerCaseValue64 = MemoryMarshal.Cast<byte, long>(lowerCaseValue);
        //        for (int i = 0; i < value64.Length; i++)
        //        {
        //            if ((value64[i] | 0x2020202020202020) != lowerCaseValue64[i]) return false;
        //        }
        //        for (int i = value64.Length * 8; i < value.Length; i++)
        //        {
        //            if ((value[i] | 0x20) != lowerCaseValue[i]) return false;
        //        }
        //        return true;
        //    }
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsShortAlphaIgnoreCase(in RespValue other)
        {
            ref readonly State x = ref _state, y = ref other._state;
            // 0x20 is 00100000, which is the bit which **for purely alpha** can be used
            return x.Storage == StorageKind.InlinedBytes
                & y.Storage == StorageKind.InlinedBytes
                & (x.Int64 | 0x2020202020202020) == (y.Int64 | 0x2020202020202020)
                & (x.HighInt32 | 0x20202020) == (y.HighInt32 | 0x20202020);
        }

        private void WriteValue(ref RespWriter writer, RespType type)
        {
            if (writer.Version < RespVersion.RESP3) type = writer.Downgrade(type);
            switch (type)
            {
                case RespType.Null:
                    WriteNull(ref writer, type);
                    break;
                case RespType.BlobString:
                case RespType.BlobError:
                case RespType.VerbatimString:
                    WriteBlob(ref writer, type);
                    break;
                case RespType.SimpleError:
                case RespType.SimpleString:
                case RespType.Number:
                case RespType.BigNumber:
                case RespType.Boolean:
                case RespType.Double:
                    WriteLineTerminated(ref writer, type);
                    break;
                default:
                    ThrowHelper.RespTypeNotImplemented(type);
                    break;
            }
        }

        private void WriteLineTerminated(ref RespWriter writer, RespType type)
        {
            // {type}{payload}\r\n
            if (_state.IsInlined)
            {
                writer.Write(type);
                switch (_state.Storage)
                {
                    case StorageKind.InlinedBytes:
                        writer.Write(_state.AsSpan());
                        break;
                    case StorageKind.InlinedDouble:
                        writer.Write(_state.Double);
                        break;
                    case StorageKind.InlinedInt64:
                        writer.Write(_state.Int64);
                        break;
                    case StorageKind.InlinedUInt32:
                        writer.Write(_state.UInt32);
                        break;
                    default:
                        ThrowHelper.StorageKindNotImplemented(_state.Storage);
                        break;
                }
                writer.WriteLine();
            }
            else
            {
                switch (_state.Storage)
                {
                    case StorageKind.Null: // treat like empty in this case
                    case StorageKind.Empty:
                        WriteEmpty(ref writer, type);
                        break;
                    default:
                        writer.Write(type);
                        if (TryGetBytes(out var bytes))
                        {
                            writer.Write(bytes);
                        }
                        else if (TryGetChars(out var chars))
                        {
                            writer.Write(chars);
                        }
                        else
                        {
                            ThrowHelper.StorageKindNotImplemented(_state.Storage);
                        }
                        writer.WriteLine();
                        break;
                }
            }
        }
        private void WriteBlob(ref RespWriter writer, RespType type)
        {
            // {type}{length}\r\n
            // {payload}\r\n
            // unless null, which is
            // {type}-1\r\n
            switch (_state.Storage)
            {
                case StorageKind.Null:
                    WriteNull(ref writer, type);
                    break;
                case StorageKind.Empty:
                    WriteEmpty(ref writer, type);
                    break;
                case StorageKind.InlinedBytes:
                    WriteLengthPrefix(ref writer, type, _state.PayloadLength);
                    writer.Write(_state.AsSpan());
                    writer.WriteLine();
                    break;
                default:
                    if (TryGetChars(out var chars))
                    {
                        WriteLengthPrefix(ref writer, type, (ulong)CountUtf8(chars));
                        writer.Write(chars);
                    }
                    else if (TryGetBytes(out var bytes))
                    {
                        WriteLengthPrefix(ref writer, type, (ulong)bytes.Length);
                        writer.Write(bytes);
                    }
                    else
                    {
                        ThrowHelper.StorageKindNotImplemented(_state.Storage);
                    }
                    writer.WriteLine();
                    break;
            }

            static void WriteLengthPrefix(ref RespWriter writer, RespType type, ulong length)
            {
                // {type}{length}\r\n
                writer.Write(type);
                writer.Write(length);
                writer.WriteLine();
            }
        }
        long CountUtf8(in ReadOnlySequence<char> payload)
        {
            if (payload.IsSingleSegment)
            {
                return UTF8.GetByteCount(payload.FirstSpan);
            }
            else
            {
                long count = 0;
                foreach (var segment in payload)
                    count += UTF8.GetByteCount(segment.Span);
                return count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsBlob(RespType type)
        {
            switch (type)
            {
                case RespType.BlobError:
                case RespType.BlobString:
                case RespType.VerbatimString:
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetAggregateArity(RespType type)
        {
            switch (type)
            {
                case RespType.Map:
                case RespType.Attribute:
                    return 2;
                case RespType.Array:
                case RespType.Set:
                case RespType.Push:
                    return 1;
                default:
                    return 0;
            }
        }
        private void WriteUnitAggregateInlinedBlob(ref RespWriter writer)
        {
            // {type}1\r\n
            // {sub type}{length}\r\n
            // {payload}\r\n
            writer.Write(writer.Downgrade(_state.Type));
            writer.Write(OneNewLine);
            writer.Write(writer.Downgrade(_state.SubType));
            writer.Write(_state.PayloadLength);
            writer.WriteLine();
            writer.Write(_state.AsSpan());
            writer.WriteLine();
        }

        static void WriteEmpty(ref RespWriter writer, RespType type)
        {
            // {type}0\r\n\r\n
            // or
            // {type}\r\n
            writer.Write(type);
            if (IsBlob(type)) writer.Write(ZeroNewLine);
            writer.WriteLine();
        }
        private static ReadOnlySpan<byte> ZeroNewLine =>
            new byte[] { (byte)'0', (byte)'\r', (byte)'\n' };

        private static ReadOnlySpan<byte> Resp3Null =>
            new byte[] { (byte)'_', (byte)'\r', (byte)'\n' };
        private static ReadOnlySpan<byte> Resp2NullPayload =>
            new byte[] { (byte)'-', (byte)'1', (byte)'\r', (byte)'\n' };
        static void WriteNull(ref RespWriter writer, RespType type)
        {
            if (writer.Version >= RespVersion.RESP3)
            {
                // _\r\n is the only null in RESP3
                writer.Write(Resp3Null);
            }
            else
            {
                if (type == RespType.Null)
                {   // this'll have to do
                    type = RespType.BlobString;
                }
                // {type}-1\r\n
                // (then the payload)
                writer.Write(type);
                writer.Write(Resp2NullPayload);
            }
        }

        private static ReadOnlySpan<byte> OneNewLine =>
            new byte[] { (byte)'1', (byte)'\r', (byte)'\n' };

        private static void WriteUnitAggregate(ref RespWriter writer, RespType aggregateType, in RespValue value)
        {
            // {type}1\r\n
            // (then the payload)
            writer.Write(aggregateType);
            writer.Write(OneNewLine);
            value.Write(ref writer);
        }

        public static bool TryParse(ReadOnlySequence<byte> input, out RespValue value, out SequencePosition end, out long bytes)
        {
            var reader = new SequenceReader<byte>(input);
            if (TryParse(ref reader, out value))
            {
                end = reader.Position;
                bytes = reader.Consumed;
                return true;
            }
            end = default;
            value = default;
            bytes = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfError()
        {
            switch (Type)
            {
                case RespType.BlobError:
                case RespType.SimpleError:
                    ThrowError();
                    break;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowError() => throw new RespException(ToString());

        private static bool TryParse(ref SequenceReader<byte> input, out RespValue value)
        {
            if (input.TryRead(out var prefix))
            {
                var type = ParseType(prefix);
                switch (type)
                {
                    case RespType.BlobError:
                    case RespType.BlobString:
                    case RespType.VerbatimString:
                        return TryParseBlob(ref input, type, out value);
                    case RespType.Double:
                    case RespType.Boolean:
                    case RespType.Null:
                    case RespType.SimpleString:
                    case RespType.SimpleError:
                    case RespType.Number:
                    case RespType.BigNumber:
                        return TryParseLineTerminated(ref input, type, out value);
                    case RespType.Array:
                    case RespType.Set:
                    case RespType.Push:
                        return TryParseAggregate(ref input, type, out value, 1);
                    case RespType.Map:
                    case RespType.Attribute:
                        return TryParseAggregate(ref input, type, out value, 2);
                    default:
                        ThrowHelper.RespTypeNotImplemented(type);
                        break;
                }
            }
            value = default;
            return false;
        }

        static bool TryReadToEndOfLine(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> payload)
        {
            if (reader.TryReadTo(out payload, (byte)'\r'))
            {
                if (!reader.TryRead(out var n)) return false;
                if (n == '\n') return true;
                ThrowHelper.ExpectedNewLine(n);
            }
            return false;
        }

        private static bool TryParseLineTerminated(ref SequenceReader<byte> input, RespType type, out RespValue message)
        {
            if (TryReadToEndOfLine(ref input, out var payload))
            {
                message = Create(type, payload);
                return true;
            }
            message = default;
            return false;
        }

        private static bool TryParseAggregate(ref SequenceReader<byte> input, RespType type, out RespValue message, int multiplier)
        {
            if (TryReadLength(ref input, out var length))
            {
                if (length == -1)
                {
                    message = new RespValue(new State(type));
                    return true;
                }
                if (length == 0)
                {
                    message = new RespValue(
                        new State(type, StorageKind.Empty, 0, 0));
                    return true;
                }
                length *= multiplier;
                if (length == 1)
                {
                    if (TryParse(ref input, out var unary))
                    {
                        if (unary._state.CanWrap)
                        {
                            message = new RespValue(unary._state.Wrap(type));
                        }
                        else
                        {
                            message = new RespValue(new State(type,
                                StorageKind.ArraySegmentRespValue, 0, 1),
                                new[] { unary });
                        }
                        return true;
                    }
                }
                var arr = new RespValue[length];
                message = new RespValue(
                    new State(type, StorageKind.ArraySegmentRespValue, 0, length), arr);
                for (int i = 0; i < length; i++)
                {
                    if (!TryParse(ref input, out arr[i])) return false;
                }
                return true;
            }
            message = default;
            return false;
        }

        private static bool TryReadLength(ref SequenceReader<byte> input, out int length)
        {
            if (TryReadToEndOfLine(ref input, out var payload))
            {
                int bytes;
                if (payload.IsSingleSegment)
                {
                    if (!Utf8Parser.TryParse(payload.First.Span, out length, out bytes)) ThrowHelper.Format();
                }
                else if (payload.Length <= 20)
                {
                    Span<byte> local = stackalloc byte[20];
                    payload.CopyTo(local);
                    if (!Utf8Parser.TryParse(local, out length, out bytes)) ThrowHelper.Format();
                }
                else
                {
                    ThrowHelper.Format();
                    length = bytes = 0;
                }
                if (bytes != payload.Length)
                    ThrowHelper.Format();
                return true;
            }
            length = default;
            return false;
        }

        private static bool TryParseBlob(ref SequenceReader<byte> input, RespType type, out RespValue message)
        {
            if (TryReadLength(ref input, out var length))
            {
                switch (length)
                {
                    case -1:
                        message = new RespValue(new State(type));
                        return true;
                    case 0:
                        if (TryAssertCRLF(ref input))
                        {
                            message = new RespValue(new State(type, StorageKind.Empty, 0, 0));
                            return true;
                        }
                        break;
                    default:
                        if (input.Remaining >= length + 2)
                        {
                            if (length <= State.InlineSize)
                            {
                                var state = new State((byte)length, type);
                                if (!input.TryCopyTo(state.AsWritableSpan())) ThrowHelper.Argument(nameof(length));
                                message = new RespValue(state);
                            }
                            else
                            {
                                var arr = new byte[length];
                                message = new RespValue(
                                    new State(type, StorageKind.ArraySegmentByte, 0, length), arr);
                                if (!input.TryCopyTo(arr)) return false;
                            }

                            input.Advance(length); // note we already checked length
                            if (TryAssertCRLF(ref input)) return true;
                        }
                        break;
                }
            }
            message = default;
            return false;
        }

        static bool TryAssertCRLF(ref SequenceReader<byte> input)
        {
            if (input.TryRead(out var b))
            {
                if (b != (byte)'\r') ThrowHelper.ExpectedNewLine(b);
                if (input.TryRead(out b))
                {
                    if (b != (byte)'\n') ThrowHelper.ExpectedNewLine(b);
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte GetPrefix(RespType type) => (byte)type;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RespType ParseType(byte value) => (RespType)value;

        public static RespValue CreateAggregate(RespType type, params RespValue[] values)
        {
            var arity = GetAggregateArity(type);
            if (arity == 0) ThrowHelper.Argument(nameof(type));
            if (values == null) return new RespValue(new State(type));
            if (values.Length == 0) return new RespValue(new State(type, StorageKind.Empty, 0, 0));

            if ((values.Length % arity) != 0) ThrowHelper.Argument(nameof(values));
            if (values.Length == 1 && values[0]._state.CanWrap) return new RespValue(values[0]._state.Wrap(type));

            return new RespValue(new State(type, StorageKind.ArraySegmentRespValue, 0, values.Length), values);
        }

        public static RespValue Create(RespType type, string value)
        {
            if (GetAggregateArity(type) != 0) ThrowHelper.Argument(nameof(type));
            if (value == null) return new RespValue(new State(type));
            int len;
            if (value.Length == 0)
            {
                return new RespValue(new State(type, StorageKind.Empty, 0, 0));
            }
            if (value.Length <= State.InlineSize && (len = UTF8.GetByteCount(value)) <= State.InlineSize)
            {
                var state = new State((byte)len, type);
                UTF8.GetBytes(value.AsSpan(), state.AsWritableSpan());
                return new RespValue(state);
            }
            return new RespValue(new State(type, StorageKind.StringSegment, 0, value.Length), value);
        }

        public static RespValue Create(RespType type, long value)
        {
            if (GetAggregateArity(type) != 0) ThrowHelper.Argument(nameof(type));
            return new RespValue(new State(type, value));
        }

        public static RespValue Create(RespType type, double value)
        {
            if (GetAggregateArity(type) != 0) ThrowHelper.Argument(nameof(type));
            return new RespValue(new State(type, value));
        }

        public static RespValue Null { get; } = new RespValue(new State(RespType.Null));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RespValue Preserve()
            => _obj0 == null & _obj1 == null ? this // nothing external!
            : PreserveSlow();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private RespValue PreserveSlow()
        {
            switch (_state.Storage)
            {
                case StorageKind.StringSegment:
                case StorageKind.Utf8StringSegment:
                    return this; // they're immutable
                case StorageKind.ArraySegmentRespValue:
                    return this; // only implemented using isolated arrays
                case StorageKind.ArraySegmentByte:
                case StorageKind.MemoryManagerByte:
                case StorageKind.SequenceSegmentByte:
                    if (TryGetBytes(out var bytes))
                    {
                        var arr = bytes.ToArray();
                        return new RespValue(new State(_state.Type, StorageKind.ArraySegmentByte, 0, arr.Length, _state.SubType), arr);
                    }
                    break;
            }
            ThrowHelper.StorageKindNotImplemented(_state.Storage);
            return default;
        }
    }
}
