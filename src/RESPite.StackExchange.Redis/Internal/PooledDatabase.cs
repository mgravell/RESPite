using Respite;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal sealed partial class PooledDatabase : PooledBase
    {
        private readonly PooledMultiplexer _parent;
        private readonly CancellationToken _cancellationToken;

        public PooledDatabase(PooledMultiplexer parent, int db, in CancellationToken cancellationToken) : base(db < 0 ? (parent.Configuration.DefaultDatabase ?? 0) : db)
        {
            _parent = parent;
            _cancellationToken = cancellationToken;
        }

        protected override Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null)
            => _parent.CallAsync(args, _cancellationToken, inspector);
        protected override Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
            => _parent.CallAsync<T>(args, selector, _cancellationToken);
        protected internal override IConnectionMultiplexer Multiplexer => _parent;
    }
    internal abstract partial class PooledBase
    {
        protected internal abstract IConnectionMultiplexer Multiplexer { get; }
        IConnectionMultiplexer IRedisAsync.Multiplexer => Multiplexer;
        public int Database { get; }

        protected PooledBase(int database)
        {
            Database = database;
        }

        bool IRedisAsync.TryWait(Task task) => task.Wait(Multiplexer.TimeoutMilliseconds);

        void IRedisAsync.Wait(Task task) => Multiplexer.Wait(task);

        T IRedisAsync.Wait<T>(Task<T> task) => Multiplexer.Wait(task);

        void IRedisAsync.WaitAll(params Task[] tasks) => Multiplexer.WaitAll(tasks);

        TimeSpan IRedis.Ping(CommandFlags flags) => Multiplexer.Wait(((IRedisAsync)this).PingAsync(flags));

        IBatch IDatabase.CreateBatch(object asyncState) => new PooledBatch(this);
        ITransaction IDatabase.CreateTransaction(object asyncState) => throw new NotImplementedException();

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => (IEnumerable<HashEntry>)((IDatabaseAsync)this).HashScanAsync(key, pattern, pageSize, 0, 0, flags);
        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => (IEnumerable<HashEntry>)((IDatabaseAsync)this).HashScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => (IEnumerable<RedisValue>)((IDatabaseAsync)this).SetScanAsync(key, pattern, pageSize, 0, 0, flags);
        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
           => (IEnumerable<RedisValue>)((IDatabaseAsync)this).SetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);

        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => (IEnumerable<SortedSetEntry>)((IDatabaseAsync)this).SortedSetScanAsync(key, pattern, pageSize, 0, 0, flags);
        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
           => (IEnumerable<SortedSetEntry>)((IDatabaseAsync)this).SortedSetScanAsync(key, pattern, pageSize, cursor, pageOffset, flags);
    }
}