using System;
using System.Threading;
using System.Threading.Tasks;

namespace Respite.Redis
{
    public static class RedisRespExtensions
    {
        public static TimeSpan Ping(this RespConnection connection)
        {
            var before = DateTime.UtcNow;
            connection.Send(RespValue.Ping);
            using var pong = connection.Receive();
            var after = DateTime.UtcNow;
            if (!pong.Value.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        public static async ValueTask<TimeSpan> PingAsync(this RespConnection connection, CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await connection.SendAsync(RespValue.Ping, cancellationToken).ConfigureAwait(false);
            using var pong = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.Value.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        private static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static readonly RespValue Pong = "pong";
    }
}
