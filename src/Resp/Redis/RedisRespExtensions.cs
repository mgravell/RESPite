using System;
using System.Buffers;
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

        //TODO: add IEnumerable<T> variant
        public static Lifetime<ReadOnlyMemory<RespFrame>> Batch(this RespConnection connection, ReadOnlyMemory<RespFrame> values)
        {
            if (values.IsEmpty) return default;

            connection.Send(in values.Span[0]); // send the first immediately
            int len = values.Length;
            if (len > 1) BeginSendInBackground(connection, values.Slice(1));
            var arr = ArrayPool<RespFrame>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                arr[i] = connection.Receive();
            }
            return new Lifetime<ReadOnlyMemory<RespFrame>>(new ReadOnlyMemory<RespFrame>(arr, 0, len),
                (val, state) => ArrayPool<RespFrame>.Shared.Return((RespFrame[])state), arr);

            static void BeginSendInBackground(RespConnection connection, ReadOnlyMemory<RespFrame> values)
                => Task.Run(async () =>
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        await connection.SendAsync(values.Span[i]).ConfigureAwait(false);
                    }
                });
        }

        //TODO: add IAsyncEnumerable<T> variant
        public static async ValueTask<Lifetime<ReadOnlyMemory<RespFrame>>> BatchAsync(this RespConnection connection, ReadOnlyMemory<RespFrame> values)
        {
            if (values.IsEmpty) return default;

            await connection.SendAsync(values.Span[0]).ConfigureAwait(false); // send the first immediately
            int len = values.Length;
            if (len > 1) BeginSendInBackground(connection, values.Slice(1));
            var arr = ArrayPool<RespFrame>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                arr[i] = await connection.ReceiveAsync().ConfigureAwait(false);
            }
            return new Lifetime<ReadOnlyMemory<RespFrame>>(new ReadOnlyMemory<RespFrame>(arr, 0, len),
                (_, state) => ArrayPool<RespFrame>.Shared.Return((RespFrame[])state), arr);

            static void BeginSendInBackground(RespConnection connection, ReadOnlyMemory<RespFrame> values)
                => Task.Run(async () =>
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        await connection.SendAsync(values.Span[i]).ConfigureAwait(false);
                    }
                });
        }
    }
}
