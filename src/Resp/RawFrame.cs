using Resp.Internal;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        public static IMemoryOwner<RawFrame> Rent(int length)
            => LeasedArray<RawFrame>.Rent(length);

        public RawFrame(FrameType type, ReadOnlySequence<byte> value)
        {
            Type = type;
            Value = value;
            _subItems = null;
        }

        public RawFrame(FrameType type, IMemoryOwner<RawFrame> subItems)
        {
            if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
            Type = type;
            Value = default;
            _subItems = subItems;
        }

        public RawFrame(FrameType type, IReadOnlyCollection<RawFrame> subItems)
        {
            if (subItems == null) ThrowHelper.ArgumentNull(nameof(subItems));
            Type = type;
            Value = default;
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
