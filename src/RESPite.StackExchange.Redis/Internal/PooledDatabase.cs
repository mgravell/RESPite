using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal sealed partial class PooledDatabase
    {
        private readonly PooledMultiplexer _parent;
        private readonly CancellationToken _cancellationToken;
        public int Database { get; }

        public PooledDatabase(PooledMultiplexer parent, int db, in CancellationToken cancellationToken)
        {
            Database = db < 0 ? (parent.Configuration.DefaultDatabase ?? 0) : db;
            _parent = parent;
            _cancellationToken = cancellationToken;
        }

        IConnectionMultiplexer IRedisAsync.Multiplexer => _parent;

        bool IRedisAsync.TryWait(Task task) => task.Wait(_parent.TimeoutMilliseconds);

        void IRedisAsync.Wait(Task task) => ((IConnectionMultiplexer)_parent).Wait(task);

        T IRedisAsync.Wait<T>(Task<T> task) => ((IConnectionMultiplexer)_parent).Wait(task);

        void IRedisAsync.WaitAll(params Task[] tasks) => ((IConnectionMultiplexer)_parent).WaitAll(tasks);

        TimeSpan IRedis.Ping(CommandFlags flags) => _parent.Wait(((IRedisAsync)this).PingAsync(flags));

        IBatch IDatabase.CreateBatch(object asyncState) => throw new NotImplementedException();
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