using Resp.Internal;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Resp
{

    public enum FrameType : byte
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

    internal enum FrameStorageKind : byte
    {
        Unknown,
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

        ArraySegmentFrame, // overlapped = offset/length, obj0 = array
        MemoryManagerFrame, // overlapped = offset/length, obj0 = manager
        SequenceSegmentFrame, // overlapped = start index/end index, obj0 = start segment, obj1 = end segment
    }

    public readonly struct RawFrame
    {
        private readonly ulong _overlapped64;
        private readonly FrameStorageKind _storage;
        private readonly FrameType _type, _subType;
        private readonly byte _aux;
        private readonly object _obj0, _obj1;

        public static readonly RawFrame Ping = Command("PING");

        private static Encoding ASCII => Encoding.ASCII;

        private RawFrame(ulong overlapped64, FrameStorageKind storage, FrameType type,
            object obj0 = null, object obj1 = null, byte aux = 0, FrameType subType = FrameType.Unknown)
        {
            _overlapped64 = overlapped64;
            _storage = storage;
            _type = type;
            _aux = aux;
            _obj0 = obj0;
            _obj1 = obj1;
            _subType = subType;
        }
        // pack two 32s into a 64, without widening concerns
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Overlap(int x, int y) => ((ulong)((uint)x) << 32) | (uint)y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> AsSpan(ref ulong value)
            => MemoryMarshal.CreateSpan(ref Unsafe.As<ulong, byte>(ref value), sizeof(ulong));

        public static ulong EncodeShortASCII(string value)
        {
            ulong val = 0;
            ASCII.GetBytes(value.AsSpan(), AsSpan(ref val));
            return val;
        }

        private static RawFrame Command(string command)
        {
            var len = ASCII.GetByteCount(command);
            if (len <= sizeof(ulong))
            {
                ulong val = EncodeShortASCII(command);
                return new RawFrame(val, FrameStorageKind.InlinedBytes, FrameType.Array, aux: (byte)len, subType: FrameType.BlobString);
            }
            else
            {
                var arr = ASCII.GetBytes(command);
                return new RawFrame(Overlap(0, len), FrameStorageKind.ArraySegmentByte, FrameType.Array, arr, subType: FrameType.BlobString);
            }
        }

        private static RawFrame Create(FrameType type, ReadOnlySequence<byte> payload)
        {
            var len = payload.Length;
            if (len <= sizeof(ulong))
            {
                ulong val = 0;
                if (len != 0) payload.CopyTo(AsSpan(ref val));
                return new RawFrame(val, FrameStorageKind.InlinedBytes, type, aux: (byte)len);
            }
            else if (payload.IsSingleSegment)
            {
                var memory = payload.First;
                if (MemoryMarshal.TryGetArray(memory, out var segment))
                {
                    return new RawFrame(Overlap(segment.Offset, segment.Count), FrameStorageKind.ArraySegmentByte, type, segment.Array);
                }
                else if (MemoryMarshal.TryGetMemoryManager(memory, out MemoryManager<byte> manager, out var start, out var length))
                {
                    return new RawFrame(Overlap(start, length), FrameStorageKind.MemoryManagerByte, type, manager);
                }
            }
            SequencePosition seqStart = payload.Start, seqEnd = payload.End;
            if (seqStart.GetObject() is ReadOnlySequenceSegment<byte> segStart
                && seqEnd.GetObject() is ReadOnlySequenceSegment<byte> segEnd)
            {
                return new RawFrame(Overlap(seqStart.GetInteger(), seqEnd.GetInteger()),
                    FrameStorageKind.SequenceSegmentByte, type, segStart, segEnd);
            }
            ThrowHelper.UnknownSequenceVariety();
            return default;
        }

        public void Write(IBufferWriter<byte> output)
        {
            if (IsAggregate(_type))
            {
                switch (_storage)
                {
                    case FrameStorageKind.ArraySegmentFrame:
                    case FrameStorageKind.MemoryManagerFrame:
                    case FrameStorageKind.SequenceSegmentFrame:
                        ThrowHelper.FrameStorageKindNotImplemented(_storage);
                        break;
                    case FrameStorageKind.InlinedBytes when IsBlob(_subType):
                        WriteUnitAggregateInlinedBlob(output);
                        break;
                    default:
                        WriteUnitAggregate(output);
                        break;
                }
            }
            else
            {
                WriteValue(output, _type);
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
        public bool IsShortAlphaIgnoreCase(ulong encoded)
        {
            // 0x20 is 00100000, which is the bit which **for purely alpha** can be used
            return _storage == FrameStorageKind.InlinedBytes &
                (_overlapped64 | 0x2020202020202020) == (encoded | 0x2020202020202020);
        }

        private void WriteValue(IBufferWriter<byte> output, FrameType type)
        {
            switch (type)
            {
                case FrameType.BlobString:
                case FrameType.BlobError:
                case FrameType.VerbatimString:
                    WriteBlob(output, type);
                    break;
                default:
                    ThrowHelper.FrameTypeNotImplemented(type);
                    break;
            }
        }

        private void WriteBlob(IBufferWriter<byte> output, FrameType type)
        {
            // {type}{length}\r\n
            // {payload}\r\n
            switch (_storage)
            {
                case FrameStorageKind.InlinedBytes:
                    // length is 1 byte, payload is max 8 bytes, so: max 14 bytes
                    var span = output.GetSpan(14);
                    span[0] = GetPrefix(type);
                    span[1] = _aux;
                    span[2] = (byte)'\r';
                    span[3] = (byte)'\n';
                    var payload = _overlapped64;
                    MemoryMarshal.CreateSpan(ref Unsafe.As<ulong, byte>(ref payload), _aux).CopyTo(span.Slice(4));
                    span[4 + _aux] = (byte)'\r';
                    span[5 + _aux] = (byte)'\n';
                    break;
                default:
                    ThrowHelper.FrameStorageKindNotImplemented(_storage);
                    break;
            }
        }

        private static ReadOnlySpan<byte> UnitAggregateBlobStringPrfix =>
            new byte[] { (byte)'\0', (byte)'1', (byte)'\r', (byte)'\n', (byte)'\0', (byte)'\0', (byte)'\r', (byte)'\n' };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsBlob(FrameType frameType)
        {
            switch(frameType)
            {
                case FrameType.BlobError:
                case FrameType.BlobString:
                case FrameType.VerbatimString:
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAggregate(FrameType frameType)
        {
            switch(frameType)
            {
                case FrameType.Array:
                case FrameType.Attribute:
                case FrameType.Map:
                case FrameType.Set:
                case FrameType.Push:
                    return true;
                default:
                    return false;
            }
        }
        private void WriteUnitAggregateInlinedBlob(IBufferWriter<byte> output)
        {
            // {type}1\r\n
            // {sub type}{length}\r\n
            // {payload}\r\n
            var span = output.GetSpan(18);
            UnitAggregateBlobStringPrfix.CopyTo(span);
            span[0] = GetPrefix(_type);
            span[4] = GetPrefix(_subType);
            span[5] = (byte)(_aux + (byte)'0');
            var payload = _overlapped64;
            MemoryMarshal.CreateSpan(ref Unsafe.As<ulong, byte>(ref payload), _aux).CopyTo(span.Slice(8));
            span[8 + _aux] = (byte)'\r';
            span[9 + _aux] = (byte)'\n';
            output.Advance(10 + _aux);
        }
        private void WriteUnitAggregate(IBufferWriter<byte> output)
        {
            // {type}1\r\n
            // (then the payload)
            var span = output.GetSpan(4);
            span[0] = GetPrefix(_type);
            span[1] = (byte)'1';
            span[2] = (byte)'\r';
            span[3] = (byte)'\n';
            output.Advance(4);
            WriteValue(output, (FrameType)_aux);
            //// {type}1\r\n = 4
            //// {subtype}{bytes, max 8}\r\n = 11
            //// {payload}\r\n
            //var span = output.GetSpan(32);
            //SimpleStringCommandPrefix.CopyTo(span);
            //int offset = 5 + WriteInt64AssumeSpace(span, value.Length, 5);
            //span[offset++] = (byte)'\r';
            //span[offset++] = (byte)'\n';
            //output.Advance(offset);
            //WriteLineTerminated(output, in value);
        }
        //private static void WriteBlob(IBufferWriter<byte> output, FrameType type, in ReadOnlySequence<byte> value)
        //{
        //    // {prefix}{length}\r\n
        //    var span = output.GetSpan(32);
        //    span[0] = GetPrefix(type);
        //    span[1] = (byte)'\r';
        //    span[2] = (byte)'\n';
        //    output.Advance(3 + WriteInt64AssumeSpace(span, value.Length, 3));
        //    WriteLineTerminated(output, in value);
        //}

        //private static void WriteLineTerminated(IBufferWriter<byte> output, in ReadOnlySequence<byte> value)
        //{

        //    if (value.IsSingleSegment)
        //    {
        //        output.Write(value.First.Span);
        //    }
        //    else
        //    {
        //        foreach (var segment in value)
        //            output.Write(segment.Span);
        //    }
        //    output.Write(NewLine);
        //}

        //private static int WriteInt64AssumeSpace(Span<byte> span, long value, int offset)
        //{
        //    if (value >= 0 && value < 10)
        //    {
        //        span[offset] = (byte)(value + '0');
        //        return 1;
        //    }
        //    ThrowHelper.NotImplemented();
        //    return -1;
        //}

        //private static ReadOnlySpan<byte> NewLine => new byte[] { (byte)'\r', (byte)'\n' };
        //private static ReadOnlySpan<byte> SimpleStringCommandPrefix => new byte[] { (byte)'*', (byte)'1', (byte)'\r', (byte)'\n', (byte)'$' };



        public static bool TryParse(ReadOnlySequence<byte> input, out RawFrame frame, out SequencePosition end)
        {
            end = default;
            if (!input.IsEmpty)
            {
                var frameType = ParseType(input.First.Span[0]);
                switch (frameType)
                {
                    case FrameType.Double:
                    case FrameType.Boolean:
                    case FrameType.Null:
                    case FrameType.SimpleString:
                    case FrameType.SimpleError:
                    case FrameType.Number:
                    case FrameType.BigNumber:
                        return TryParseLineTerminated(input, frameType, out frame, ref end);
                    default:
                        ThrowHelper.FrameTypeNotImplemented(frameType);
                        break;
                }
            }
            frame = default;
            return false;
        }

        private static bool TryParseLineTerminated(in ReadOnlySequence<byte> input, FrameType frameType, out RawFrame message, ref SequencePosition end)
        {
            var sequenceReader = new SequenceReader<byte>(input);

            if (sequenceReader.TryReadTo(out ReadOnlySequence<byte> payloadPlusPrefix, (byte)'\r')
                && sequenceReader.TryRead(out var n) && n == '\n')
            {
                message = Create(frameType, payloadPlusPrefix.Slice(1));
                end = sequenceReader.Position;
                return true;
            }
            message = default;
            return false;
        }

        //public static IMemoryOwner<RawFrame> Rent(int length)
        //    => LeasedArray<RawFrame>.Rent(length);

        //public RawFrame(FrameType type, ReadOnlySequence<byte> value)
        //{
        //    Type = type;
        //    _value = value;
        //    _subItems = null;
        //}

        //public RawFrame(FrameType type, IMemoryOwner<RawFrame> subItems)
        //{
        //    if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
        //    Type = type;
        //    _value = default;
        //    _subItems = subItems;
        //}

        //public RawFrame(FrameType type, IReadOnlyCollection<RawFrame> subItems)
        //{
        //    if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
        //    Type = type;
        //    _value = default;
        //    _subItems = LeasedArray<RawFrame>.Rent(subItems.Count);
        //    if (subItems is ICollection<RawFrame> collection && MemoryMarshal.TryGetArray<RawFrame>(_subItems.Memory, out var segment))
        //    {
        //        collection.CopyTo(segment.Array, segment.Offset);
        //    }
        //    else
        //    {
        //        var span = _subItems.Memory.Span;
        //        if (subItems is IReadOnlyList<RawFrame> rolist)
        //        {
        //            for (int i = 0; i < span.Length; i++)
        //            {
        //                span[i] = rolist[i];
        //            }
        //        }
        //        else if (subItems is IList<RawFrame> list)
        //        {
        //            for (int i = 0; i < span.Length; i++)
        //            {
        //                span[i] = list[i];
        //            }
        //        }
        //        else
        //        {
        //            int i = 0;
        //            foreach(var item in subItems)
        //            {
        //                span[i++] = item;
        //            }
        //        }
        //    }
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte GetPrefix(FrameType type) => (byte)type;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FrameType ParseType(byte value) => (FrameType)value;

        //private readonly ReadOnlySequence<byte> _value;
        //public ReadOnlySequence<byte> Value => _value;
        //public FrameType Type { get; }

        //private readonly IMemoryOwner<RawFrame> _subItems;


        //public bool IsSimple => _subItems == null;

        //public ReadOnlyMemory<RawFrame> Memory => _subItems?.Memory ?? default;
        //public ReadOnlySpan<RawFrame> Span => Memory.Span;

        //public void Dispose()
        //{
        //    if (_subItems != null)
        //    {
        //        foreach (ref readonly var item in _subItems.Memory.Span)
        //            item.Dispose();
        //        _subItems.Dispose();
        //    }
        //}
    }
}
