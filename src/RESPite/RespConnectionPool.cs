using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    public sealed class RespConnectionPool : RespConnection
    {
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

        public bool TryDetach(RespConnection connection) => _pool.TryDetach(connection);

        internal ValueTask<RespConnection> ConnectAsync(CancellationToken cancellationToken) => _factory(cancellationToken);

        const string NOT_SUPPORTED_APIS = "You must use the Call/Batch APIs";
        private static void ThrowAPIsNotSupported() => ThrowHelper.NotSupported(NOT_SUPPORTED_APIS);
        protected override Lifetime<RespValue> OnReceive() { ThrowAPIsNotSupported(); return default; }
        protected override ValueTask<Lifetime<RespValue>> OnReceiveAsync(CancellationToken cancellationToken) { ThrowAPIsNotSupported(); return default; }
        protected override void OnSend(in RespValue value) => ThrowAPIsNotSupported();
        protected override ValueTask OnSendAsync(RespValue value, CancellationToken cancellationToken) { ThrowAPIsNotSupported(); return default;}

        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new void Send(in RespValue value) => base.Send(value);
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new ValueTask SendAsync(RespValue value, CancellationToken cancellationToken = default) => base.SendAsync(value, cancellationToken);
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new Lifetime<RespValue> Receive() => base.Receive();
        [Obsolete(NOT_SUPPORTED_APIS, true)]
        public new ValueTask<Lifetime<RespValue>> ReceiveAsync(CancellationToken cancellationToken = default) => base.ReceiveAsync(cancellationToken);

        protected override ValueTask OnDisposeAsync() => _pool.DisposeAsync();

        public override async ValueTask<T> CallAsync<T>(RespValue command, Func<RespValue, T> selector, CancellationToken cancellationToken = default)
        {
            await using var lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
            await lease.Value.SendAsync(command, cancellationToken).ConfigureAwait(false);
            using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return selector(response.Value);
        }
        public override async ValueTask CallAsync(RespValue command, Action<RespValue> validator, CancellationToken cancellationToken = default)
        {
            await using var lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
            await lease.Value.SendAsync(command, cancellationToken).ConfigureAwait(false);
            using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            validator(response.Value);
        }
        public override async ValueTask<Lifetime<ReadOnlyMemory<RespValue>>> BatchAsync(ReadOnlyMemory<RespValue> values, CancellationToken cancellationToken = default)
        {
            await using var lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
            return await lease.Value.BatchAsync(values, cancellationToken).ConfigureAwait(false);
        }

        public override Lifetime<ReadOnlyMemory<RespValue>> Batch(ReadOnlyMemory<RespValue> values) => throw new NotImplementedException();

        public override void Call(in RespValue command, Action<RespValue> validator) => throw new NotImplementedException();
        public override T Call<T>(in RespValue command, Func<RespValue, T> selector) => throw new NotImplementedException();
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
