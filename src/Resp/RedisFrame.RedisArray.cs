using System;
using System.Runtime.CompilerServices;

namespace Resp
{
    public abstract class RedisArray : RedisFrame
    {
        public abstract int Count { get; }
        public abstract RedisFrame this[int index] { get; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static RedisFrame ThrowOutOfRange() => throw new IndexOutOfRangeException();

        private protected override void OnDispose()
        {
            int count = Count;
            for (int i = 0; i < count; i++)
                this[i].Dispose();
        }

        private protected RedisArray(FrameFlags flags) : base(flags) { }

        public static RedisArray Create() => RedisArrayZero.Instance;
        public static RedisArray Create(RedisFrame frame) => new RedisArrayOne(frame);

        private sealed class RedisArrayZero : RedisArray
        {
            internal static RedisArrayZero Instance { get; } = new RedisArrayZero();
            private RedisArrayZero() : base(FrameFlags.Preserved) { }
            public override int Count => 0;
            public override RedisFrame this[int index] => ThrowOutOfRange();
        }

        private sealed class RedisArrayOne : RedisArray
        {
            private readonly RedisFrame _frame;
            public override int Count => 1;
            internal RedisArrayOne(RedisFrame frame) : base(frame.Flags)
                => _frame = frame;

            public override RedisFrame this[int index] =>
                index switch
                {
                    0 => _frame,
                    _ => ThrowOutOfRange(),
                };
        }
    }
}
