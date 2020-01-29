using System;
using System.Buffers;

namespace Resp
{
    public abstract class RedisSimpleString : RedisFrame
    {
        public abstract int PayloadBytes { get; }
        public static RedisSimpleString Create(string value) => new RedisSimpleStringString(value);

        public abstract object Payload { get; }

        private protected RedisSimpleString(FrameFlags flags) : base(flags) { }

        private sealed class RedisSimpleStringString : RedisSimpleString
        {
            private readonly string _value;
            internal RedisSimpleStringString(string value) : base(FrameFlags.Preserved)
                => _value = value ?? throw new ArgumentNullException(nameof(value));

            public override int PayloadBytes => _value.Length; // ASCII

            public override object Payload => _value;
        }
    }
}
