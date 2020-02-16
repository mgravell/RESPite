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
            connection.Send(RespValue.Ping);
            var pong = connection.Receive();
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        public static async ValueTask<TimeSpan> PingAsync(this RespConnection connection, CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await connection.SendAsync(RespValue.Ping, cancellationToken).ConfigureAwait(false);
            var pong = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        private static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static readonly RespValue Pong = "pong";

        //TODO: add IEnumerable<T> variant
        public static Lifetime<ReadOnlyMemory<RespValue>> Batch(this RespConnection connection, ReadOnlyMemory<RespValue> values)
        {
            if (values.IsEmpty) return default;

            connection.Send(in values.Span[0]); // send the first immediately
            int len = values.Length;
            if (len > 1) BeginSendInBackground(connection, values.Slice(1));
            var arr = ArrayPool<RespValue>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                arr[i] = connection.Receive();
            }
            return new Lifetime<ReadOnlyMemory<RespValue>>(new ReadOnlyMemory<RespValue>(arr, 0, len),
                (val, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state), arr);

            static void BeginSendInBackground(RespConnection connection, ReadOnlyMemory<RespValue> values)
                => Task.Run(async () =>
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        await connection.SendAsync(values.Span[i]).ConfigureAwait(false);
                    }
                });
        }

        //TODO: add IAsyncEnumerable<T> variant
        public static async ValueTask<Lifetime<ReadOnlyMemory<RespValue>>> BatchAsync(this RespConnection connection, ReadOnlyMemory<RespValue> values)
        {
            if (values.IsEmpty) return default;

            await connection.SendAsync(values.Span[0]).ConfigureAwait(false); // send the first immediately
            int len = values.Length;
            Task pending = null;
            if (len > 1) pending = BeginSendInBackground(connection, values.Slice(1));
            var arr = ArrayPool<RespValue>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                arr[i] = await connection.ReceiveAsync().ConfigureAwait(false);
            }
            if (pending != null) await pending.ConfigureAwait(false);

            return new Lifetime<ReadOnlyMemory<RespValue>>(new ReadOnlyMemory<RespValue>(arr, 0, len),
                (_, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state), arr);

            static Task BeginSendInBackground(RespConnection connection, ReadOnlyMemory<RespValue> values)
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
