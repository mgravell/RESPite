using Respite;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    class LeasedDatabase : PooledBase, IAsyncDisposable
    {
        private readonly PooledMultiplexer _muxer;
        protected internal override IConnectionMultiplexer Multiplexer => _muxer;
        private readonly AsyncLifetime<RespConnection> _lease;
        private readonly CancellationToken _cancellationToken;

        protected override async Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null)
        {
            using (args)
            {
                _muxer.IncrementOpCount();
                await _lease.Value.SendAsync(RespValue.CreateAggregate(RespType.Array, args.Value), _cancellationToken).ConfigureAwait(false);
            }
            using var response = await _lease.Value.ReceiveAsync(_cancellationToken).ConfigureAwait(false);
            response.Value.ThrowIfError();
            inspector?.Invoke(response.Value);
        }
        protected override async Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
        {
            using (args)
            {
                _muxer.IncrementOpCount();
                await _lease.Value.SendAsync(RespValue.CreateAggregate(RespType.Array, args.Value), _cancellationToken).ConfigureAwait(false);
            }
            using var response = await _lease.Value.ReceiveAsync(_cancellationToken).ConfigureAwait(false);
            response.Value.ThrowIfError();
            return selector(response.Value);
        }

        private LeasedDatabase(IDatabase db, PooledMultiplexer muxer, AsyncLifetime<RespConnection> lease, CancellationToken cancellationToken) : base(db.Database)
        {
            _muxer = muxer;
            _lease = lease;
            _cancellationToken = cancellationToken;
        }

        internal static ValueTask<AsyncLifetime<IDatabase>> CreateAsync(IDatabase db, CancellationToken cancellationToken)
        {
            if (db.Multiplexer is PooledMultiplexer pooled) return Impl(db, pooled, cancellationToken);
            return new ValueTask<AsyncLifetime<IDatabase>>(new AsyncLifetime<IDatabase>(db));

            static async ValueTask<AsyncLifetime<IDatabase>> Impl(IDatabase db, PooledMultiplexer muxer, CancellationToken cancellationToken)
            {
                var lease = await muxer.RentAsync(cancellationToken).ConfigureAwait(false);
                var leasedDb = new LeasedDatabase(db, muxer, lease, cancellationToken);
                return new AsyncLifetime<IDatabase>(leasedDb, obj => ((LeasedDatabase)obj).DisposeAsync());
            }
        }

        public ValueTask DisposeAsync() => _lease.DisposeAsync();
    }
}
