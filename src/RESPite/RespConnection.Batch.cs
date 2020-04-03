using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    partial class RespConnection
    {
        //TODO: add IEnumerable<T> variant
        public virtual Lifetime<ReadOnlyMemory<RespValue>> Batch(ReadOnlyMemory<RespValue> values)
        {
            if (values.IsEmpty) return default;

            this.Send(in values.Span[0]); // send the first immediately
            int len = values.Length;
            if (len > 1) BeginSendInBackground(this, values.Slice(1));
            var arr = ArrayPool<RespValue>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                using var lifetime = this.Receive();
                arr[i] = lifetime.Value.Preserve();
            }
            return new Lifetime<ReadOnlyMemory<RespValue>>(new ReadOnlyMemory<RespValue>(arr, 0, len),
                (val, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state!), arr);

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
        public virtual async ValueTask<Lifetime<ReadOnlyMemory<RespValue>>> BatchAsync(ReadOnlyMemory<RespValue> values, CancellationToken cancellationToken = default)
        {
            if (values.IsEmpty) return default;

            await this.SendAsync(values.Span[0], cancellationToken).ConfigureAwait(false); // send the first immediately
            int len = values.Length;
            Task? pending = null;
            if (len > 1) pending = BeginSendInBackground(this, values.Slice(1), cancellationToken);
            var arr = ArrayPool<RespValue>.Shared.Rent(values.Length);
            for (int i = 0; i < len; i++)
            {
                using var lifetime = await this.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                arr[i] = lifetime.Value.Preserve();
            }
            if (pending != null) await pending.ConfigureAwait(false);

            return new Lifetime<ReadOnlyMemory<RespValue>>(new ReadOnlyMemory<RespValue>(arr, 0, len),
                (_, state) => ArrayPool<RespValue>.Shared.Return((RespValue[])state!), arr);

            static Task BeginSendInBackground(RespConnection connection, ReadOnlyMemory<RespValue> values, CancellationToken cancellationToken)
                => Task.Run(async () =>
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        await connection.SendAsync(values.Span[i], cancellationToken).ConfigureAwait(false);
                    }
                });
        }
    }
}
