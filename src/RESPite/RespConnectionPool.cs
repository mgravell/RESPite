using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    public sealed class RespConnectionPool : IAsyncDisposable
    {
        private readonly Pool<RespConnection> _pool;
        public ValueTask DisposeAsync() => _pool.DisposeAsync();
        private readonly Func<CancellationToken, ValueTask<RespConnection>> _factory;

        public int ConnectionCount => _pool.Count;
        public int TotalConnectionCount => _pool.TotalCount;

        public RespConnectionPool(Func<CancellationToken, ValueTask<RespConnection>> factory, RespConnectionPoolOptions? options = null)
        {
            if (factory == null) ThrowHelper.ArgumentNull(nameof(factory));
            options ??= RespConnectionPoolOptions.Default;
            _pool = new Pool<RespConnection>(options.Options, this);
            _factory = factory!;
        }

        public ValueTask<AsyncLifetime<RespConnection>> RentAsync(CancellationToken cancellationToken = default)
            => _pool.RentAsync(cancellationToken);

        internal ValueTask<RespConnection> ConnectAsync(CancellationToken cancellationToken) => _factory(cancellationToken);
    }

    public sealed class RespConnectionPoolOptions
    {
        public static RespConnectionPoolOptions Default { get; } = new RespConnectionPoolOptions(
            PoolOptions<RespConnection>.DefaultMinCount, PoolOptions<RespConnection>.DefaultMaxCount);
        
        internal PoolOptions<RespConnection> Options { get; }

        public static RespConnectionPoolOptions Create(int minCount = PoolOptions<RespConnection>.DefaultMinCount, int maxCount = PoolOptions<RespConnection>.DefaultMaxCount)
            => minCount == PoolOptions<RespConnection>.DefaultMinCount && maxCount == PoolOptions<RespConnection>.DefaultMaxCount
            ? Default : new RespConnectionPoolOptions(minCount, maxCount);

        private RespConnectionPoolOptions(int minCount, int maxCount)
            => Options = PoolOptions<RespConnection>.Create(minCount, maxCount,
                (state, cancellationToken) => state is RespConnectionPool pool ? pool.ConnectAsync(cancellationToken) : default,
                (_, conn) => conn.DisposeAsync(),
                (_, conn) => !conn.IsDoomed);
    }
}
