using Resp.Internal;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Resp
{
    partial struct RespValue
    {
        [StructLayout(LayoutKind.Explicit, Pack = 0, Size = 16)]
        readonly struct State : IEquatable<State>
        {
            public override bool Equals(object obj) => obj is State typed && Equals(typed);
            public bool Equals(in State other)
                => Int64 == other.Int64 & HighInt64 == other.HighInt64;
            bool IEquatable<State>.Equals(State other) => Equals(in other);
            public override int GetHashCode() => Int64.GetHashCode() ^ HighInt64.GetHashCode();
            public override string ToString() => Type.ToString();
            public State(RespType type, RespType subType = RespType.Unknown) : this()
            {
                Storage = StorageKind.Null;
                Type = type;
                SubType = subType;
            }

            public State Unwrap() => new State(in this);
            private State(in State parent)
            {
                this = parent;
                Type = parent.SubType;
                SubType = RespType.Unknown;
            }
            public State Wrap(RespType type) => new State(in this, type);
            private State(in State child, RespType type)
            {
                this = child;
                Type = type;
                SubType = child.Type;
            }

            public State(RespType type, StorageKind storage, int startOffset,
                int endOffsetOrLength, RespType subType = RespType.Unknown) : this()
            {
                Storage = storage;
                StartOffset = startOffset;
                EndOffset = endOffsetOrLength;
                Type = type;
                SubType = subType;
            }

            public State(RespType type, double value, RespType subType = RespType.Unknown) : this()
            {
                Double = value;
                Storage = StorageKind.InlinedDouble;
                Type = type;
                SubType = subType;
            }

            public State(RespType type, long value, RespType subType = RespType.Unknown) : this()
            {
                Int64 = value;
                Storage = StorageKind.InlinedInt64;
                Type = type;
                SubType = subType;
            }

            public State(RespType type, uint value, RespType subType = RespType.Unknown) : this()
            {
                UInt32 = value;
                Storage = StorageKind.InlinedUInt32;
                Type = type;
                SubType = subType;
            }

            public State(byte length, RespType type, RespType subType = RespType.Unknown) : this()
            {
                Storage = StorageKind.InlinedBytes;
                PayloadLength = length;
                Type = type;
                SubType = subType;
            }

            internal Span<byte> AsWritableSpan()
                => MemoryMarshal.CreateSpan(
                    ref Unsafe.As<long, byte>(ref Unsafe.AsRef(in Int64)), PayloadLength);


            internal ReadOnlySpan<byte> AsSpan()
                => MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<long, byte>(ref Unsafe.AsRef(in Int64)), PayloadLength);

            public const int InlineSize = 12;

            [FieldOffset(0)]
            public readonly double Double;
            [FieldOffset(0)]
            public readonly long Int64;
            [FieldOffset(0)]
            public readonly uint UInt32;
            [FieldOffset(0)]
            public readonly int StartOffset;
            [FieldOffset(4)]
            public readonly int Length;
            [FieldOffset(4)]
            public readonly int EndOffset;

            [FieldOffset(8)]
            public readonly long HighInt64;
            [FieldOffset(8)]
            public readonly int HighInt32;

            [FieldOffset(12)]
            public readonly byte PayloadLength;
            [FieldOffset(13)]
            public readonly RespType Type;
            [FieldOffset(14)]
            public readonly RespType SubType;
            [FieldOffset(15)]
            public readonly StorageKind Storage;

            public bool IsInlined
            {
                get
                {
                    switch (Storage)
                    {
                        case StorageKind.InlinedBytes:
                        case StorageKind.InlinedDouble:
                        case StorageKind.InlinedInt64:
                        case StorageKind.InlinedUInt32:
                            return true;
                        default:
                            return false;
                    }
                }
            }
        }
    }
}
