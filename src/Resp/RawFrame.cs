using Resp.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Resp
{

    public enum FrameType : byte
    {
        Unknown,
        BlobString,
        SimpleString,
        SimpleError,
        Number,
        Null,
        Double,
        Boolean,
        BlobError,
        VerbatimString,
        BigNumber,
        Array,
        Map,
        Set,
        Attribute,
        Push,
    }

    public readonly struct RawFrame : IDisposable
    {
        public static readonly RawFrame Ping = Command("PING");

        private static RawFrame Command(string command)
        {
            var bytes = Encoding.ASCII.GetBytes(command);
            return new RawFrame(FrameType.Array, new ReadOnlySequence<byte>(bytes));
        }

        public static bool TryParse(ReadOnlySequence<byte> input, out RawFrame frame, out SequencePosition end)
        {
            bool isValid = false;
            frame = default;
            end = default;
            try
            {
                // this = is correct; intent is to avoid dispose
                return isValid = TryParseRaw(input, out frame, ref end);
            }
            finally
            {
                if (!isValid) frame.Dispose();
            }
        }

        public void Write(IBufferWriter<byte> output)
        {
            switch (Type)
            {

                //case FrameType.SimpleString:
                //case FrameType.SimpleError:
                //case FrameType.Number:
                //case FrameType.BigNumber:
                //    WriteLineTerminated(output, Type, Value);
                //    break;
                case FrameType.BlobString:
                case FrameType.BlobError:
                    WriteBlob(output, Type, in _value);
                    break;
                case FrameType.Array:
                    if (IsSimple)
                    {
                        WriteUnitBlobStringArray(output, in _value);
                    }
                    else
                    {
                        ThrowHelper.NotImplemented();
                    }
                    break;

                default:
                    ThrowHelper.FrameTypeNotImplemented(Type);
                    break;
            }
        }

        public bool IsShortAlphaIgnoreCase(ReadOnlySpan<byte> lowerCaseValue)
        {
            var val = _value;
            if (val.Length != lowerCaseValue.Length) return false;

            return _value.IsSingleSegment ? Equals(_value.First.Span, lowerCaseValue)
                : CopyLocalEquals(_value, lowerCaseValue);

            static bool Equals(ReadOnlySpan<byte> value, ReadOnlySpan<byte> lowerCaseValue)
            {
                switch (value.Length)
                {
                    // 0x20 is 00100000, which is the bit which **for purely alpha** can be used
                    // to lower-casify; note that for non-alpha, this is an invalid thing, so
                    // this should *only* be used when the comparison value is known to be alpha
                    case 2: // OK
                        return (BitConverter.ToInt16(value) | 0x2020) == BitConverter.ToInt16(lowerCaseValue);
                    case 4: // PONG
                        return (BitConverter.ToInt32(value) | 0x20202020) == BitConverter.ToInt32(lowerCaseValue);
                    default:
                        for (int i = 0; i < value.Length; i++)
                        {
                            if ((value[i] | 0x20) != lowerCaseValue[i]) return false;
                        }
                        return true;
                }
                
            }

            static bool CopyLocalEquals(in ReadOnlySequence<byte> value, ReadOnlySpan<byte> lowerCaseValue)
            {
                Span<byte> local = stackalloc byte[lowerCaseValue.Length];
                value.CopyTo(local);
                return Equals(local, lowerCaseValue);
            }
        }

        private static void WriteUnitBlobStringArray(IBufferWriter<byte> output, in ReadOnlySequence<byte> value)
        {
            // this is an array of length 1 masquarading as a simple value

            // *1\r\n${bytes}\r\n{payload}\r\n
            var span = output.GetSpan(32);
            SimpleStringCommandPrefix.CopyTo(span);
            int offset = 5 + WriteInt64AssumeSpace(span, value.Length, 5);
            span[offset++] = (byte)'\r';
            span[offset++] = (byte)'\n';
            output.Advance(offset);
            WriteLineTerminated(output, in value);
        }
        private static void WriteBlob(IBufferWriter<byte> output, FrameType type, in ReadOnlySequence<byte> value)
        {
            // {prefix}{length}\r\n
            var span = output.GetSpan(32);
            span[0] = GetPrefix(type);
            span[1] = (byte)'\r';
            span[2] = (byte)'\n';
            output.Advance(3 + WriteInt64AssumeSpace(span, value.Length, 3));
            WriteLineTerminated(output, in value);
        }

        private static void WriteLineTerminated(IBufferWriter<byte> output, in ReadOnlySequence<byte> value)
        {

            if (value.IsSingleSegment)
            {
                output.Write(value.First.Span);
            }
            else
            {
                foreach (var segment in value)
                    output.Write(segment.Span);
            }
            output.Write(NewLine);
        }

        private static int WriteInt64AssumeSpace(Span<byte> span, long value, int offset)
        {
            if (value >= 0 && value < 10)
            {
                span[offset] = (byte)(value + '0');
                return 1;
            }
            ThrowHelper.NotImplemented();
            return -1;
        }

        private static ReadOnlySpan<byte> NewLine => new byte[] { (byte)'\r', (byte)'\n' };
        private static ReadOnlySpan<byte> SimpleStringCommandPrefix => new byte[] { (byte)'*', (byte)'1', (byte)'\r', (byte)'\n', (byte)'$' };



        private static bool TryParseRaw(ReadOnlySequence<byte> input, out RawFrame frame, ref SequencePosition end)
        {
            if (!input.IsEmpty)
            {
                var frameType = ParseType(input.First.Span[0]);
                switch (frameType)
                {
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
                message = new RawFrame(frameType, payloadPlusPrefix.Slice(1));
                end = sequenceReader.Position;
                return true;
            }
            message = default;
            return false;
        }

        public static IMemoryOwner<RawFrame> Rent(int length)
            => LeasedArray<RawFrame>.Rent(length);

        public RawFrame(FrameType type, ReadOnlySequence<byte> value)
        {
            Type = type;
            _value = value;
            _subItems = null;
        }

        public RawFrame(FrameType type, IMemoryOwner<RawFrame> subItems)
        {
            if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
            Type = type;
            _value = default;
            _subItems = subItems;
        }

        public RawFrame(FrameType type, IReadOnlyCollection<RawFrame> subItems)
        {
            if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
            Type = type;
            _value = default;
            _subItems = LeasedArray<RawFrame>.Rent(subItems.Count);
            if (subItems is ICollection<RawFrame> collection && MemoryMarshal.TryGetArray<RawFrame>(_subItems.Memory, out var segment))
            {
                collection.CopyTo(segment.Array, segment.Offset);
            }
            else
            {
                var span = _subItems.Memory.Span;
                if (subItems is IReadOnlyList<RawFrame> rolist)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = rolist[i];
                    }
                }
                else if (subItems is IList<RawFrame> list)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        span[i] = list[i];
                    }
                }
                else
                {
                    int i = 0;
                    foreach(var item in subItems)
                    {
                        span[i++] = item;
                    }
                }
            }
        }

        internal static byte GetPrefix(FrameType type)
            => (byte)("\0$+-:_,#!=(*%~|>"[(int)type]);

        internal static FrameType ParseType(byte value)
        => value switch
        {
            // simple types
            (byte)'$' => FrameType.BlobString,
            (byte)'+' => FrameType.SimpleString,
            (byte)'-' => FrameType.SimpleError,
            (byte)':' => FrameType.Number,
            (byte)'_' => FrameType.Null,
            (byte)',' => FrameType.Double,
            (byte)'#' => FrameType.Boolean,
            (byte)'!' => FrameType.BlobString,
            (byte)'=' => FrameType.VerbatimString,
            (byte)'(' => FrameType.BigNumber,

            // aggregated types
            (byte)'*' => FrameType.Array,
            (byte)'%' => FrameType.Map,
            (byte)'~' => FrameType.Set,
            (byte)'|' => FrameType.Attribute,
            (byte)'>' => FrameType.Push,
            // oops!
            _ => FrameType.Unknown,

            // also note:
            // ? = unbounded length marker
            // ; = streamed string continuation
            // . = terminator for unbounded aggregated types
        };

        private readonly ReadOnlySequence<byte> _value;
        public ReadOnlySequence<byte> Value => _value;
        public FrameType Type { get; }

        private readonly IMemoryOwner<RawFrame> _subItems;


        public bool IsSimple => _subItems == null;

        public ReadOnlyMemory<RawFrame> Memory => _subItems?.Memory ?? default;
        public ReadOnlySpan<RawFrame> Span => Memory.Span;

        public void Dispose()
        {
            if (_subItems != null)
            {
                foreach (ref readonly var item in _subItems.Memory.Span)
                    item.Dispose();
                _subItems.Dispose();
            }
        }
    }
}
