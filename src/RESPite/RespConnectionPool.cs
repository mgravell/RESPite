using PooledAwait;
using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    public sealed class RespConnectionPool : RespConnection
    {
        public override bool PreferSync => false;

        private readonly Pool<RespConnection> _pool;
        private readonly Func<CancellationToken, ValueTask<RespConnection>> _factory;

        public int ConnectionCount => _pool.Count;
        public int TotalConnectionCount => _pool.TotalCount;

        public RespConnectionPool(Func<CancellationToken, ValueTask<RespConnection>> factory, RespConnectionPoolOptions? options = null, object? state = null) : base(state)
        {
            if (factory == null) ThrowHelper.ArgumentNull(nameof(factory));
            options ??= RespConnectionPoolOptions.Default;
            _pool = new Pool<RespConnection>(options.Options, this);
            _factory = factory!;
        }

        public ValueTask<AsyncLifetime<RespConnection>> RentAsync(CancellationToken cancellationToken = default)
            => _pool.RentAsync(cancellationToken);

        public Lifetime<RespConnection> Rent()
            => _pool.Rent();

        public bool TryDetach(RespConnection connection) => _pool.TryDetach(connection);

        internal ValueTask<RespConnection> ConnectAsync(CancellationToken cancellationToken) => _factory(cancellationToken);

        const string NOT_SUPPORTED_APIS = "You must use the Call/Batch APIs";
        private static void ThrowAPIsNotSupported() => ThrowHelper.NotSupported(NOT_SUPPORTED_APIS);
        protected override Lifetime<RespValue> OnReceive() { ThrowAPIsNotSupported(); return default; }
        protected override ValueTask<Lifetime<RespValue>> OnReceiveAsync(CancellationToken cancellationToken) { ThrowAPIsNotSupported(); return default; }
        protected override void OnSend(in RespValue value, bool flush) => ThrowAPIsNotSupported();
        protected override ValueTask OnSendAsync(RespValue value, bool flush, CancellationToken cancellationToken) { ThrowAPIsNotSupported(); return default;}

        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new void Send(in RespValue value, bool flush) => base.Send(value, flush);
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new ValueTask SendAsync(RespValue value, CancellationToken cancellationToken = default, bool flush = true) => base.SendAsync(value, cancellationToken, flush);
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new Lifetime<RespValue> Receive() => base.Receive();
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new ValueTask<Lifetime<RespValue>> ReceiveAsync(CancellationToken cancellationToken = default) => base.ReceiveAsync(cancellationToken);

        protected override ValueTask OnDisposeAsync() => _pool.DisposeAsync();

        public override ValueTask<T> CallAsync<T>(RespValue command, Func<RespValue, T> selector, CancellationToken cancellationToken = default)
        {
            return Impl(_pool, command, selector, cancellationToken);
            static async PooledValueTask<T> Impl(Pool<RespConnection> pool, RespValue command, Func<RespValue, T> selector, CancellationToken cancellationToken)
            {
                await using var lease = await pool.RentAsync(cancellationToken).ConfigureAwait(false);
                await lease.Value.SendAsync(command, cancellationToken).ConfigureAwait(false);
                using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                return selector(response.Value);
            }
        }
        public override ValueTask CallAsync(RespValue command, Action<RespValue> validator, CancellationToken cancellationToken = default)
        {
            return Impl(_pool, command, validator, cancellationToken);
            static async PooledValueTask Impl(Pool<RespConnection> pool, RespValue command, Action<RespValue> validator, CancellationToken cancellationToken)
            {
                await using var lease = await pool.RentAsync(cancellationToken).ConfigureAwait(false);
                await lease.Value.SendAsync(command, cancellationToken).ConfigureAwait(false);
                using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                validator(response.Value);
            }
        }
        public override ValueTask<Lifetime<ReadOnlyMemory<RespValue>>> BatchAsync(ReadOnlyMemory<RespValue> values, CancellationToken cancellationToken = default)
        {
            return Impl(_pool, values, cancellationToken);
            static async PooledValueTask<Lifetime<ReadOnlyMemory<RespValue>>> Impl(Pool<RespConnection> pool, ReadOnlyMemory<RespValue> values, CancellationToken cancellationToken)
            {
                await using var lease = await pool.RentAsync(cancellationToken).ConfigureAwait(false);
                return await lease.Value.BatchAsync(values, cancellationToken).ConfigureAwait(false);
            }
        }

        public override Lifetime<ReadOnlyMemory<RespValue>> Batch(ReadOnlyMemory<RespValue> values)
        {
            using var lease = _pool.Rent();
            return lease.Value.Batch(values);
        }

        public override void Call(in RespValue command, Action<RespValue> validator)
        {
            using var lease = _pool.Rent();
            lease.Value.Send(command);
            using var response = lease.Value.Receive();
            validator(response.Value);
        }
        public override T Call<T>(in RespValue command, Func<RespValue, T> selector)
        {
            using var lease = _pool.Rent();
            lease.Value.Send(command);
            using var response = lease.Value.Receive();
            return selector(response.Value);
        }
    }

    public sealed class RespConnectionPoolOptions
    {
        public static RespConnectionPoolOptions Default { get; } = new RespConnectionPoolOptions(PoolOptions<RespConnection>.DefaultMaxCount);
        
        internal PoolOptions<RespConnection> Options { get; }

        public static RespConnectionPoolOptions Create(int maxCount = PoolOptions<RespConnection>.DefaultMaxCount)
            => maxCount == PoolOptions<RespConnection>.DefaultMaxCount
            ? Default : new RespConnectionPoolOptions(maxCount);

        private RespConnectionPoolOptions(int maxCount)
            => Options = PoolOptions<RespConnection>.Create(maxCount,
                (state, cancellationToken) => state is RespConnectionPool pool ? pool.ConnectAsync(cancellationToken) : default,
                (_, conn) => conn.DisposeAsync(),
                (_, conn) => conn.IsReusable);
    }
}
