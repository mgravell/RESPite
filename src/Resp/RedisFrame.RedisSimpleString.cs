using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Resp
{
    public abstract class RedisSimpleString : RedisFrame
    {
        public static RedisSimpleString Empty => RedisSimpleStringEmpty.Instance;

        public abstract int PayloadBytes { get; }

        public static RedisSimpleString Create(string value)
            => value.Length == 0 ? Empty : new RedisSimpleStringString(value);

        public virtual bool IsBytes(out ReadOnlySpan<byte> value)
        {
            value = default;
            return false;
        }
        public virtual bool IsChars(out ReadOnlySpan<char> value)
        {
            value = default;
            return false;
        }
        public virtual bool IsString(out string value)
        {
            value = default;
            return false;
        }

        public unsafe static RedisSimpleString Create(ReadOnlySpan<byte> value)
        {
            if (value.IsEmpty) return Empty;

            var lease = ArrayPool<byte>.Shared.Rent(value.Length);
            value.CopyTo(lease);
            return new RedisSimpleStringMemory(FrameFlags.None, new ReadOnlyMemory<byte>(lease, 0, value.Length));
        }

        private RedisSimpleString(FrameFlags flags) : base(flags) { }
        private sealed class RedisSimpleStringEmpty : RedisSimpleString
        {
            public static RedisSimpleString Instance {get;} = new RedisSimpleStringEmpty();
            private RedisSimpleStringEmpty() : base(FrameFlags.Preserved) { }

            public override int PayloadBytes => 0;
            public override string ToString() => "";

            public override bool IsBytes(out ReadOnlySpan<byte> value)
            {
                value = default;
                return true;
            }
            public override bool IsChars(out ReadOnlySpan<char> value)
            {
                value = default;
                return true;
            }
            public override bool IsString(out string value)
            {
                value = "";
                return true;
            }
        }

        private sealed class RedisSimpleStringString : RedisSimpleString
        {
            private readonly string _value;

            public override int PayloadBytes => Encoding.ASCII.GetByteCount(_value);

            internal RedisSimpleStringString(string value) : base(FrameFlags.None)
                => _value = value;

            public override string ToString() => _value;

            public override bool IsChars(out ReadOnlySpan<char> value)
            {
                value = _value.AsSpan();
                return true;
            }
            public override bool IsString(out string value)
            {
                value = _value;
                return true;
            }
        }

        private sealed class RedisSimpleStringMemory : RedisSimpleString
        {
            private readonly ReadOnlyMemory<byte> _value;

            public override int PayloadBytes => _value.Length;

            internal RedisSimpleStringMemory(FrameFlags flags, ReadOnlyMemory<byte> value) : base(flags)
                => _value = value;

            public override string ToString() => Encoding.ASCII.GetString(_value.Span);

            public override bool IsBytes(out ReadOnlySpan<byte> value)
            {
                value = _value.Span;
                return true;
            }

            private protected override void OnDispose()
            {
                if (MemoryMarshal.TryGetArray(_value, out var segment) && segment.Offset == 0)
                    ArrayPool<byte>.Shared.Return(segment.Array);
            }
        }
    }
}
