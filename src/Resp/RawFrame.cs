using System;
using System.Buffers;
using System.Collections.Generic;

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

    public readonly struct RawFrame
    {
        public RawFrame(FrameType type, ReadOnlySequence<byte> value)
        {
            Type = type;
            Value = value;
            SubItems = Array.Empty<RawFrame>();
        }

        public RawFrame(FrameType type, IReadOnlyList<RawFrame> subItems)
        {
            Type = type;
            Value = default;
            SubItems = subItems ?? Array.Empty<RawFrame>();
        }



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

        public ReadOnlySequence<byte> Value { get; }
        public FrameType Type { get; }

        public IReadOnlyList<RawFrame> SubItems { get; }
    }
}
