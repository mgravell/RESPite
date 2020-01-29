using System;

namespace Resp
{
    public abstract partial class RedisFrame : IDisposable
    {
        [Flags]
        internal enum FrameFlags
        {
            None,
            Preserved,
        }
        internal FrameFlags Flags { get; }
        private protected RedisFrame(FrameFlags flags) => Flags = flags;
        private protected RedisFrame() { }

        public void Dispose()
        {
            if ((Flags & FrameFlags.Preserved) == 0) OnDispose();
        }

        private protected virtual void OnDispose() { }
    }
}
