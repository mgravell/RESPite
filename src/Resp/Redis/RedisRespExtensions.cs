using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp.Redis
{
    public static class RedisRespExtensions
    {
        public static TimeSpan Ping(this RespConnection connection)
        {
            var before = DateTime.UtcNow;
            connection.Send(RespFrame.Ping);
            var pong = connection.Receive();
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        public static async ValueTask<TimeSpan> PingAsync(this RespConnection connection, CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await connection.SendAsync(RespFrame.Ping, cancellationToken).ConfigureAwait(false);
            var pong = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        private static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static readonly ulong Pong = RespFrame.EncodeShortASCII("pong");
    }
}
