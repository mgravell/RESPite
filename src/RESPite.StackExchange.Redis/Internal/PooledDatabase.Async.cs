using Respite;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal partial class PooledBase : IDatabaseAsync
    {
        protected abstract Task CallAsync(Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null);
        protected abstract Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector);

        Task<RedisValue> IDatabaseAsync.DebugObjectAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisResult> IDatabaseAsync.ExecuteAsync(string command, params object[] args)
        {
            return CallAsync(Args(command, args), val => AsRedisResult(val));
            static RedisResult AsRedisResult(in RespValue value) => RedisResult.Create(ToRedisValue(value));
        }

        private static RedisValue ToRedisValue(in RespValue value)
        {
            return value.Type switch
            {
                RespType.SimpleString => value.ToString(),
                RespType.Number => value.ToInt64(),
                RespType.Double => value.ToDouble(),
                RespType.Boolean => value.ToBoolean(),
                _ => SnapshotBytes(value),
            };
            static RedisValue SnapshotBytes(in RespValue value)
            {
                byte[] blob = new byte[value.GetByteCount()];
                value.CopyTo(blob);
                return blob;
            }
        }
        static readonly RespValue s_OK = RespValue.Create(RespType.SimpleString, "ok");
        private static bool IsOK(in RespValue value) => value.EqualsAsciiIgnoreCase(s_OK);

        Task<RedisResult> IDatabaseAsync.ExecuteAsync(string command, ICollection<object> args, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double?> IDatabaseAsync.GeoDistanceAsync(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<string[]> IDatabaseAsync.GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<string> IDatabaseAsync.GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<GeoPosition?[]> IDatabaseAsync.GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<GeoPosition?> IDatabaseAsync.GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<GeoRadiusResult[]> IDatabaseAsync.GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<GeoRadiusResult[]> IDatabaseAsync.GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HashDecrementAsync(RedisKey key, RedisValue hashField, long value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double> IDatabaseAsync.HashDecrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.HashDeleteAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HashDeleteAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.HashExistsAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<HashEntry[]> IDatabaseAsync.HashGetAllAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.HashGetAsync(RedisKey key, RedisValue[] hashFields, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<Lease<byte>> IDatabaseAsync.HashGetLeaseAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HashIncrementAsync(RedisKey key, RedisValue hashField, long value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double> IDatabaseAsync.HashIncrementAsync(RedisKey key, RedisValue hashField, double value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.HashKeysAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HashLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        IAsyncEnumerable<HashEntry> IDatabaseAsync.HashScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.HashSetAsync(RedisKey key, HashEntry[] hashFields, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HashStringLengthAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.HashValuesAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.HyperLogLogAddAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.HyperLogLogAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HyperLogLogLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.HyperLogLogLengthAsync(RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.HyperLogLogMergeAsync(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.HyperLogLogMergeAsync(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<EndPoint> IDatabaseAsync.IdentifyEndpointAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        bool IDatabaseAsync.IsConnected(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyDeleteAsync(RedisKey key, CommandFlags flags)
            => CallAsync(Args("unlink", key), val => val.ToInt64() != 0);

        Task<long> IDatabaseAsync.KeyDeleteAsync(RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<byte[]> IDatabaseAsync.KeyDumpAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyExistsAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.KeyExistsAsync(RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyExpireAsync(RedisKey key, TimeSpan? expiry, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyExpireAsync(RedisKey key, DateTime? expiry, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<TimeSpan?> IDatabaseAsync.KeyIdleTimeAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.KeyMigrateAsync(RedisKey key, EndPoint toServer, int toDatabase, int timeoutMilliseconds, MigrateOptions migrateOptions, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyMoveAsync(RedisKey key, int database, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyPersistAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisKey> IDatabaseAsync.KeyRandomAsync(CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyRenameAsync(RedisKey key, RedisKey newKey, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.KeyRestoreAsync(RedisKey key, byte[] value, TimeSpan? expiry, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<TimeSpan?> IDatabaseAsync.KeyTimeToLiveAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.KeyTouchAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.KeyTouchAsync(RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisType> IDatabaseAsync.KeyTypeAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.ListGetByIndexAsync(RedisKey key, long index, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListInsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListInsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.ListLeftPopAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListLeftPushAsync(RedisKey key, RedisValue value, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListLeftPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.ListRangeAsync(RedisKey key, long start, long stop, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListRemoveAsync(RedisKey key, RedisValue value, long count, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.ListRightPopAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.ListRightPopLeftPushAsync(RedisKey source, RedisKey destination, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListRightPushAsync(RedisKey key, RedisValue value, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.ListRightPushAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.ListSetByIndexAsync(RedisKey key, long index, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task IDatabaseAsync.ListTrimAsync(RedisKey key, long start, long stop, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.LockExtendAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.LockQueryAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.LockReleaseAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.LockTakeAsync(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        RespValue Command(string value) => RespValue.Create(RespType.BlobString, value);

        Lifetime<Memory<RespValue>> Args(string command, in RedisKey key)
        {
            var lease = RespValue.Lease(2);
            var span = lease.Value.Span;
            span[0] = Command(command);
            span[1] = ToBlobString(key);
            return lease;
        }
        Lifetime<Memory<RespValue>> Args(string command, in RedisKey key, long value)
        {
            var lease = RespValue.Lease(2);
            var span = lease.Value.Span;
            span[0] = Command(command);
            span[1] = ToBlobString(key);
            span[2] = ToBlobString(value);
            return lease;
        }
        Lifetime<Memory<RespValue>> Args(string command, in RedisKey key, double value)
        {
            var lease = RespValue.Lease(2);
            var span = lease.Value.Span;
            span[0] = Command(command);
            span[1] = ToBlobString(key);
            span[2] = ToBlobString(value);
            return lease;
        }

        Lifetime<Memory<RespValue>> Args(string command)
        {
            var lease = RespValue.Lease(1);
            lease.Value.Span[0] = Command(command);
            return lease;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RespValue ToBlobString(in RedisKey value) => RespValue.Create(RespType.BlobString, (byte[])value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RespValue ToBlobString(in RedisValue value) => RespValue.Create(RespType.BlobString, (byte[])value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RespValue ToBlobString(string value) => RespValue.Create(RespType.BlobString, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RespValue ToBlobString(long value) => RespValue.Create(RespType.BlobString, value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static RespValue ToBlobString(double value) => RespValue.Create(RespType.BlobString, value);

        Lifetime<Memory<RespValue>> Args(string command, object[] args)
        {
            args ??= Array.Empty<object>();
            var lease = RespValue.Lease(1 + args.Length);
            var span = lease.Value.Span;
            span[0] = Command(command);
            for (int i = 0; i < args.Length; i++)
            {
                span[i + 1] = (args[i]) switch
                {
                    string s => ToBlobString(s),
                    int i32 => ToBlobString(i32),
                    long i64 => ToBlobString(i64),
                    double f64 => ToBlobString(f64),
                    float f32 => ToBlobString(f32),
                    RedisKey key => ToBlobString(key),
                    RedisValue value => ToBlobString(value),
                    byte[] blob => RespValue.Create(RespType.BlobString, blob),
                    _ => throw new NotSupportedException(),
                };
            }
            return lease;
        }

        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        async Task<TimeSpan> IRedisAsync.PingAsync(CommandFlags flags)
        {
            var start = Stopwatch.GetTimestamp();
            await CallAsync(Args("ping")).ConfigureAwait(false);
            var end = Stopwatch.GetTimestamp();
            var ticks = (long)(TimestampToTicks * (end - start));
            return new TimeSpan(ticks);
        }

        Task<long> IDatabaseAsync.PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisResult> IDatabaseAsync.ScriptEvaluateAsync(string script, RedisKey[] keys, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisResult> IDatabaseAsync.ScriptEvaluateAsync(byte[] hash, RedisKey[] keys, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisResult> IDatabaseAsync.ScriptEvaluateAsync(LuaScript script, object parameters, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisResult> IDatabaseAsync.ScriptEvaluateAsync(LoadedLuaScript script, object parameters, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SetLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SetMembersAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.SetPopAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SetPopAsync(RedisKey key, long count, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.SetRandomMemberAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        IAsyncEnumerable<RedisValue> IDatabaseAsync.SetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortAndStoreAsync(RedisKey destination, RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[] get, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SortAsync(RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[] get, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights, Aggregate aggregate, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double> IDatabaseAsync.SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double> IDatabaseAsync.SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetLengthAsync(RedisKey key, double min, double max, Exclude exclude, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<SortedSetEntry?> IDatabaseAsync.SortedSetPopAsync(RedisKey key, Order order, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<SortedSetEntry[]> IDatabaseAsync.SortedSetPopAsync(RedisKey key, long count, Order order, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SortedSetRangeByRankAsync(RedisKey key, long start, long stop, Order order, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<SortedSetEntry[]> IDatabaseAsync.SortedSetRangeByRankWithScoresAsync(RedisKey key, long start, long stop, Order order, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SortedSetRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<SortedSetEntry[]> IDatabaseAsync.SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long?> IDatabaseAsync.SortedSetRankAsync(RedisKey key, RedisValue member, Order order, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        IAsyncEnumerable<SortedSetEntry> IDatabaseAsync.SortedSetScanAsync(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double?> IDatabaseAsync.SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StreamAddAsync(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StreamAddAsync(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamEntry[]> IDatabaseAsync.StreamClaimAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue[]> IDatabaseAsync.StreamClaimIdsOnlyAsync(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StreamConsumerGroupSetPositionAsync(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamConsumerInfo[]> IDatabaseAsync.StreamConsumerInfoAsync(RedisKey key, RedisValue groupName, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StreamCreateConsumerGroupAsync(RedisKey key, RedisValue groupName, RedisValue? position, bool createStream, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamDeleteAsync(RedisKey key, RedisValue[] messageIds, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamDeleteConsumerAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StreamDeleteConsumerGroupAsync(RedisKey key, RedisValue groupName, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamGroupInfo[]> IDatabaseAsync.StreamGroupInfoAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamInfo> IDatabaseAsync.StreamInfoAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamPendingInfo> IDatabaseAsync.StreamPendingAsync(RedisKey key, RedisValue groupName, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamPendingMessageInfo[]> IDatabaseAsync.StreamPendingMessagesAsync(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId, RedisValue? maxId, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamEntry[]> IDatabaseAsync.StreamRangeAsync(RedisKey key, RedisValue? minId, RedisValue? maxId, int? count, Order messageOrder, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamEntry[]> IDatabaseAsync.StreamReadAsync(RedisKey key, RedisValue position, int? count, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisStream[]> IDatabaseAsync.StreamReadAsync(StreamPosition[] streamPositions, int? countPerStream, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamEntry[]> IDatabaseAsync.StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<StreamEntry[]> IDatabaseAsync.StreamReadGroupAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, bool noAck, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisStream[]> IDatabaseAsync.StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisStream[]> IDatabaseAsync.StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, bool noAck, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StreamTrimAsync(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringAppendAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringDecrementAsync(RedisKey key, long value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<double> IDatabaseAsync.StringDecrementAsync(RedisKey key, double value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StringGetAsync(RedisKey key, CommandFlags flags)
            => CallAsync(Args("get", key), val => ToRedisValue(val));

        Task<RedisValue[]> IDatabaseAsync.StringGetAsync(RedisKey[] keys, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StringGetBitAsync(RedisKey key, long offset, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<Lease<byte>> IDatabaseAsync.StringGetLeaseAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StringGetRangeAsync(RedisKey key, long start, long end, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StringGetSetAsync(RedisKey key, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValueWithExpiry> IDatabaseAsync.StringGetWithExpiryAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<long> IDatabaseAsync.StringIncrementAsync(RedisKey key, long value, CommandFlags flags)
            => CallAsync(value == 1 ? Args("incr", key) : Args("incrby", key, value),
                val => val.ToInt64());

        Task<double> IDatabaseAsync.StringIncrementAsync(RedisKey key, double value, CommandFlags flags)
            => CallAsync(Args("incrbyfloat", key, value), val => val.ToDouble());

        Task<long> IDatabaseAsync.StringLengthAsync(RedisKey key, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StringSetAsync(KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDatabaseAsync.StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags)
        {
            throw new NotImplementedException();
        }

        Task<RedisValue> IDatabaseAsync.StringSetRangeAsync(RedisKey key, long offset, RedisValue value, CommandFlags flags)
        {
            throw new NotImplementedException();
        }
    }
}
