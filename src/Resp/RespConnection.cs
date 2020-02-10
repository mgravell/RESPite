using Pipelines.Sockets.Unofficial;
using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    public abstract class RespConnection
    {
        public static RespConnection Create(Stream stream) => new StreamRespConnection(stream);
        public static RespConnection Create(Socket socket) => new StreamRespConnection(new NetworkStream(socket, true));
        public abstract void Send(in RawFrame frame);
        public abstract RawFrame Receive();
        public abstract ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken = default);
        public abstract ValueTask<RawFrame> ReceiveAsync(CancellationToken cancellationToken = default);

        public TimeSpan Ping()
        {
            var before = DateTime.UtcNow;
            Send(RawFrame.Ping);
            var pong = Receive();
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }
        public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await SendAsync(RawFrame.Ping, cancellationToken).ConfigureAwait(false);
            var pong = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static readonly ulong Pong = RawFrame.EncodeShortASCII("pong");

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowCanceled() => throw new OperationCanceledException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowEndOfStream() => throw new EndOfStreamException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowAborted() => throw new ConnectionAbortedException();

        //public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        //{
        //    var before = DateTime.UtcNow;
        //    await SendAsync(RedisFrame.Ping, cancellationToken).ConfigureAwait(false);
        //    using var pong = await ReadAsync(cancellationToken).ConfigureAwait(false);
        //    var after = DateTime.UtcNow;
        //    if (!(pong is RedisSimpleString rss && rss.Equals("PONG", StringComparison.OrdinalIgnoreCase))) Wat();
        //    return after - before;
        //}
    }
}
