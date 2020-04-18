using Respite;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal abstract partial class PooledBase
    {
        protected internal PooledMultiplexer Multiplexer { get; }
        IConnectionMultiplexer IRedisAsync.Multiplexer => Multiplexer;
        public int Database { get; }

        protected PooledBase(PooledMultiplexer muxer, int database)
        {
            Multiplexer = muxer;
            Database = database;
        }

        bool IRedisAsync.TryWait(Task task) => task.Wait(Multiplexer.TimeoutMilliseconds);

        void IRedisAsync.Wait(Task task) => Multiplexer.Wait(task);

        T IRedisAsync.Wait<T>(Task<T> task) => Multiplexer.Wait(task);

        void IRedisAsync.WaitAll(params Task[] tasks) => ((IConnectionMultiplexer)Multiplexer).WaitAll(tasks);

        TimeSpan IRedis.Ping(CommandFlags flags) => Multiplexer.Wait(((IRedisAsync)this).PingAsync(flags));

        IBatch IDatabase.CreateBatch(object asyncState) => new PooledBatch(this);
        ITransaction IDatabase.CreateTransaction(object asyncState) => throw new NotImplementedException();

        internal virtual async Task CallAsync(List<IBatchedOperation> operations, CancellationToken cancellationToken)
        {
            await using var lease = await Multiplexer.RentAsync(cancellationToken).ConfigureAwait(false);
            await Multiplexer.CallAsync(lease.Value, operations, cancellationToken).ConfigureAwait(false);
        }

        protected abstract Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null);
        protected abstract Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector);


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
