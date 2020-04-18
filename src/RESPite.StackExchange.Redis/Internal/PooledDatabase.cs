using Respite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal sealed partial class PooledDatabase : PooledBase
    {
        private readonly CancellationToken _cancellationToken;

        public PooledDatabase(PooledMultiplexer parent, int db, in CancellationToken cancellationToken)
            : base(parent, db < 0 ? (parent.Configuration.DefaultDatabase ?? 0) : db)
        {
            _cancellationToken = cancellationToken;
        }

        protected override Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null)
            => Multiplexer.CallAsync(args, _cancellationToken, inspector);
        protected override Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
            => Multiplexer.CallAsync<T>(args, selector, _cancellationToken);
    }
    
}