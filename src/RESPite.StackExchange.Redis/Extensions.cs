using Respite;
using RESPite.StackExchange.Redis.Internal;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis
{
    public static class Extensions
    {
        public static ValueTask<IConnectionMultiplexer> GetPooledMultiplexerAsync(
            this ConfigurationOptions configuration, int maxCount = 0, CancellationToken cancellationToken = default)
        {
            var options = maxCount <= 0 ? RespConnectionPoolOptions.Default : RespConnectionPoolOptions.Create(maxCount);
            var obj = new PooledMultiplexer(configuration, options);
            if (obj.Configuration.AbortOnConnectFail)
            {
                var db = obj.GetDatabaseAsync(cancellationToken: cancellationToken);
                return PingAsync(db);
            }
            return new ValueTask<IConnectionMultiplexer>(obj);

            static async ValueTask<IConnectionMultiplexer> PingAsync(IDatabaseAsync db)
            {
                await db.PingAsync().ConfigureAwait(false);
                return db.Multiplexer;
            }
        }

        public static ValueTask<AsyncLifetime<IDatabase>> LeaseDedicatedAsync(this IDatabase db, CancellationToken cancellationToken = default)
            => LeasedDatabase.CreateAsync(db, cancellationToken);
    }
}
