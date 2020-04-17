using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Net;

namespace RESPite.StackExchange.Redis.Internal
{
/* this wrapper is generated via:

using StackExchange.Redis;
using System;
using System.Linq;
using System.Text;

static class P
{
    static void Main()
    {
        foreach(var method in typeof(IDatabase).GetMethods().OrderBy(x => x.Name))
        {
            switch(method.Name)
            {
                case nameof(IDatabase.CreateBatch):
                case nameof(IDatabase.CreateTransaction):
                case "get_" + nameof(IDatabase.Database):
                case nameof(IDatabase.HashScan):
                case nameof(IDatabase.SetScan):
                case nameof(IDatabase.SortedSetScan):
                    continue;
            }
            Console.Write("        ");
            Console.Write(Format(method.ReturnType));
            Console.Write(" IDatabase.");
            Console.Write(method.Name);
            Console.Write("(");
            var args = method.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (i != 0) Console.Write(", ");
                Console.Write(Format(arg.ParameterType));
                Console.Write(" ");
                Console.Write(arg.Name);
            }
            Console.WriteLine(")");
            Console.Write("            => Multiplexer.Wait(((IDatabaseAsync)this).");
            Console.Write(method.Name);
            Console.Write("Async(");
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (i != 0) Console.Write(", ");
                Console.Write(arg.Name);
            }
            Console.WriteLine("));");
            Console.WriteLine();
        }
    }
    static string Format(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(long)) return "long";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(double)) return "double";
        if (type == typeof(void)) return "void";

        var nt = Nullable.GetUnderlyingType(type);
        if (nt != null) return Format(nt) + "?";

        if (type.IsArray && type.GetArrayRank() == 1)
            return Format(type.GetElementType()) + "[]";
        
        if (type.IsGenericType)
        {
            var gargs = type.GetGenericArguments();
            var gname = type.GetGenericTypeDefinition().Name;
            var sb = new StringBuilder(gname.Substring(0, gname.IndexOf('`'))).Append('<');
            for(int i = 0; i < gargs.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                sb.Append(Format(gargs[i]));
            }
            return sb.Append('>').ToString();
        }

        return type.Name;
    }
}
*/
    internal partial class PooledBase : IDatabase
    {
        RedisValue IDatabase.DebugObject(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).DebugObjectAsync(key, flags));

        RedisResult IDatabase.Execute(string command, Object[] args)
            => Multiplexer.Wait(((IDatabaseAsync)this).ExecuteAsync(command, args));

        RedisResult IDatabase.Execute(string command, ICollection<Object> args, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ExecuteAsync(command, args, flags));

        bool IDatabase.GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoAddAsync(key, longitude, latitude, member, flags));

        bool IDatabase.GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoAddAsync(key, value, flags));

        long IDatabase.GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoAddAsync(key, values, flags));

        double? IDatabase.GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoDistanceAsync(key, member1, member2, unit, flags));

        string[] IDatabase.GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoHashAsync(key, members, flags));

        string IDatabase.GeoHash(RedisKey key, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoHashAsync(key, member, flags));

        GeoPosition?[] IDatabase.GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoPositionAsync(key, members, flags));

        GeoPosition? IDatabase.GeoPosition(RedisKey key, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoPositionAsync(key, member, flags));

        GeoRadiusResult[] IDatabase.GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoRadiusAsync(key, member, radius, unit, count, order, options, flags));

        GeoRadiusResult[] IDatabase.GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit, int count, Order? order, GeoRadiusOptions options, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoRadiusAsync(key, longitude, latitude, radius, unit, count, order, options, flags));

        bool IDatabase.GeoRemove(RedisKey key, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).GeoRemoveAsync(key, member, flags));

        long IDatabase.HashDecrement(RedisKey key, RedisValue hashField, long value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashDecrementAsync(key, hashField, value, flags));

        double IDatabase.HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashDecrementAsync(key, hashField, value, flags));

        bool IDatabase.HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashDeleteAsync(key, hashField, flags));

        long IDatabase.HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashDeleteAsync(key, hashFields, flags));

        bool IDatabase.HashExists(RedisKey key, RedisValue hashField, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashExistsAsync(key, hashField, flags));

        RedisValue IDatabase.HashGet(RedisKey key, RedisValue hashField, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashGetAsync(key, hashField, flags));

        RedisValue[] IDatabase.HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashGetAsync(key, hashFields, flags));

        HashEntry[] IDatabase.HashGetAll(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashGetAllAsync(key, flags));

        Lease<byte> IDatabase.HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashGetLeaseAsync(key, hashField, flags));

        long IDatabase.HashIncrement(RedisKey key, RedisValue hashField, long value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashIncrementAsync(key, hashField, value, flags));

        double IDatabase.HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashIncrementAsync(key, hashField, value, flags));

        RedisValue[] IDatabase.HashKeys(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashKeysAsync(key, flags));

        long IDatabase.HashLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashLengthAsync(key, flags));

        void IDatabase.HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashSetAsync(key, hashFields, flags));

        bool IDatabase.HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashSetAsync(key, hashField, value, when, flags));

        long IDatabase.HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashStringLengthAsync(key, hashField, flags));

        RedisValue[] IDatabase.HashValues(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HashValuesAsync(key, flags));

        bool IDatabase.HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogAddAsync(key, value, flags));

        bool IDatabase.HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogAddAsync(key, values, flags));

        long IDatabase.HyperLogLogLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogLengthAsync(key, flags));

        long IDatabase.HyperLogLogLength(RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogLengthAsync(keys, flags));

        void IDatabase.HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogMergeAsync(destination, first, second, flags));

        void IDatabase.HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).HyperLogLogMergeAsync(destination, sourceKeys, flags));

        EndPoint IDatabase.IdentifyEndpoint(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).IdentifyEndpointAsync(key, flags));

        bool IDatabase.KeyDelete(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyDeleteAsync(key, flags));

        long IDatabase.KeyDelete(RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyDeleteAsync(keys, flags));

        byte[] IDatabase.KeyDump(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyDumpAsync(key, flags));

        bool IDatabase.KeyExists(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyExistsAsync(key, flags));

        long IDatabase.KeyExists(RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyExistsAsync(keys, flags));

        bool IDatabase.KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyExpireAsync(key, expiry, flags));

        bool IDatabase.KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyExpireAsync(key, expiry, flags));

        TimeSpan? IDatabase.KeyIdleTime(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyIdleTimeAsync(key, flags));

        void IDatabase.KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase, int timeoutMilliseconds, MigrateOptions migrateOptions, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyMigrateAsync(key, toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags));

        bool IDatabase.KeyMove(RedisKey key, int database, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyMoveAsync(key, database, flags));

        bool IDatabase.KeyPersist(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyPersistAsync(key, flags));

        RedisKey IDatabase.KeyRandom(CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyRandomAsync(flags));

        bool IDatabase.KeyRename(RedisKey key, RedisKey newKey, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyRenameAsync(key, newKey, when, flags));

        void IDatabase.KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyRestoreAsync(key, value, expiry, flags));

        TimeSpan? IDatabase.KeyTimeToLive(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyTimeToLiveAsync(key, flags));

        bool IDatabase.KeyTouch(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyTouchAsync(key, flags));

        long IDatabase.KeyTouch(RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyTouchAsync(keys, flags));

        RedisType IDatabase.KeyType(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).KeyTypeAsync(key, flags));

        RedisValue IDatabase.ListGetByIndex(RedisKey key, long index, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListGetByIndexAsync(key, index, flags));

        long IDatabase.ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListInsertAfterAsync(key, pivot, value, flags));

        long IDatabase.ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListInsertBeforeAsync(key, pivot, value, flags));

        RedisValue IDatabase.ListLeftPop(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListLeftPopAsync(key, flags));

        long IDatabase.ListLeftPush(RedisKey key, RedisValue value, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListLeftPushAsync(key, value, when, flags));

        long IDatabase.ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListLeftPushAsync(key, values, flags));

        long IDatabase.ListLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListLengthAsync(key, flags));

        RedisValue[] IDatabase.ListRange(RedisKey key, long start, long stop, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRangeAsync(key, start, stop, flags));

        long IDatabase.ListRemove(RedisKey key, RedisValue value, long count, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRemoveAsync(key, value, count, flags));

        RedisValue IDatabase.ListRightPop(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRightPopAsync(key, flags));

        RedisValue IDatabase.ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRightPopLeftPushAsync(source, destination, flags));

        long IDatabase.ListRightPush(RedisKey key, RedisValue value, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRightPushAsync(key, value, when, flags));

        long IDatabase.ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListRightPushAsync(key, values, flags));

        void IDatabase.ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListSetByIndexAsync(key, index, value, flags));

        void IDatabase.ListTrim(RedisKey key, long start, long stop, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ListTrimAsync(key, start, stop, flags));

        bool IDatabase.LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).LockExtendAsync(key, value, expiry, flags));

        RedisValue IDatabase.LockQuery(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).LockQueryAsync(key, flags));

        bool IDatabase.LockRelease(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).LockReleaseAsync(key, value, flags));

        bool IDatabase.LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).LockTakeAsync(key, value, expiry, flags));

        long IDatabase.Publish(RedisChannel channel, RedisValue message, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).PublishAsync(channel, message, flags));

        RedisResult IDatabase.ScriptEvaluate(string script, RedisKey[] keys, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ScriptEvaluateAsync(script, keys, values, flags));

        RedisResult IDatabase.ScriptEvaluate(byte[] hash, RedisKey[] keys, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ScriptEvaluateAsync(hash, keys, values, flags));

        RedisResult IDatabase.ScriptEvaluate(LuaScript script, Object parameters, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ScriptEvaluateAsync(script, parameters, flags));

        RedisResult IDatabase.ScriptEvaluate(LoadedLuaScript script, Object parameters, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).ScriptEvaluateAsync(script, parameters, flags));

        bool IDatabase.SetAdd(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetAddAsync(key, value, flags));

        long IDatabase.SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetAddAsync(key, values, flags));

        RedisValue[] IDatabase.SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetCombineAsync(operation, first, second, flags));

        RedisValue[] IDatabase.SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetCombineAsync(operation, keys, flags));

        long IDatabase.SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetCombineAndStoreAsync(operation, destination, first, second, flags));

        long IDatabase.SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetCombineAndStoreAsync(operation, destination, keys, flags));

        bool IDatabase.SetContains(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetContainsAsync(key, value, flags));

        long IDatabase.SetLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetLengthAsync(key, flags));

        RedisValue[] IDatabase.SetMembers(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetMembersAsync(key, flags));

        bool IDatabase.SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetMoveAsync(source, destination, value, flags));

        RedisValue IDatabase.SetPop(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetPopAsync(key, flags));

        RedisValue[] IDatabase.SetPop(RedisKey key, long count, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetPopAsync(key, count, flags));

        RedisValue IDatabase.SetRandomMember(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetRandomMemberAsync(key, flags));

        RedisValue[] IDatabase.SetRandomMembers(RedisKey key, long count, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetRandomMembersAsync(key, count, flags));

        bool IDatabase.SetRemove(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetRemoveAsync(key, value, flags));

        long IDatabase.SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SetRemoveAsync(key, values, flags));

        RedisValue[] IDatabase.Sort(RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[] get, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortAsync(key, skip, take, order, sortType, by, get, flags));

        long IDatabase.SortAndStore(RedisKey destination, RedisKey key, long skip, long take, Order order, SortType sortType, RedisValue by, RedisValue[] get, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortAndStoreAsync(destination, key, skip, take, order, sortType, by, get, flags));

        bool IDatabase.SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetAddAsync(key, member, score, flags));

        bool IDatabase.SortedSetAdd(RedisKey key, RedisValue member, double score, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetAddAsync(key, member, score, when, flags));

        long IDatabase.SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetAddAsync(key, values, flags));

        long IDatabase.SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetAddAsync(key, values, when, flags));

        long IDatabase.SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetCombineAndStoreAsync(operation, destination, first, second, aggregate, flags));

        long IDatabase.SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights, Aggregate aggregate, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetCombineAndStoreAsync(operation, destination, keys, weights, aggregate, flags));

        double IDatabase.SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetDecrementAsync(key, member, value, flags));

        double IDatabase.SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetIncrementAsync(key, member, value, flags));

        long IDatabase.SortedSetLength(RedisKey key, double min, double max, Exclude exclude, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetLengthAsync(key, min, max, exclude, flags));

        long IDatabase.SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetLengthByValueAsync(key, min, max, exclude, flags));

        SortedSetEntry? IDatabase.SortedSetPop(RedisKey key, Order order, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetPopAsync(key, order, flags));

        SortedSetEntry[] IDatabase.SortedSetPop(RedisKey key, long count, Order order, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetPopAsync(key, count, order, flags));

        RedisValue[] IDatabase.SortedSetRangeByRank(RedisKey key, long start, long stop, Order order, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByRankAsync(key, start, stop, order, flags));

        SortedSetEntry[] IDatabase.SortedSetRangeByRankWithScores(RedisKey key, long start, long stop, Order order, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByRankWithScoresAsync(key, start, stop, order, flags));

        RedisValue[] IDatabase.SortedSetRangeByScore(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByScoreAsync(key, start, stop, exclude, order, skip, take, flags));

        SortedSetEntry[] IDatabase.SortedSetRangeByScoreWithScores(RedisKey key, double start, double stop, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByScoreWithScoresAsync(key, start, stop, exclude, order, skip, take, flags));

        RedisValue[] IDatabase.SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByValueAsync(key, min, max, exclude, skip, take, flags));

        RedisValue[] IDatabase.SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, Order order, long skip, long take, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRangeByValueAsync(key, min, max, exclude, order, skip, take, flags));

        long? IDatabase.SortedSetRank(RedisKey key, RedisValue member, Order order, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRankAsync(key, member, order, flags));

        bool IDatabase.SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRemoveAsync(key, member, flags));

        long IDatabase.SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRemoveAsync(key, members, flags));

        long IDatabase.SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRemoveRangeByRankAsync(key, start, stop, flags));

        long IDatabase.SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRemoveRangeByScoreAsync(key, start, stop, exclude, flags));

        long IDatabase.SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetRemoveRangeByValueAsync(key, min, max, exclude, flags));

        double? IDatabase.SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).SortedSetScoreAsync(key, member, flags));

        long IDatabase.StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamAcknowledgeAsync(key, groupName, messageId, flags));

        long IDatabase.StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamAcknowledgeAsync(key, groupName, messageIds, flags));

        RedisValue IDatabase.StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamAddAsync(key, streamField, streamValue, messageId, maxLength, useApproximateMaxLength, flags));

        RedisValue IDatabase.StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId, int? maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamAddAsync(key, streamPairs, messageId, maxLength, useApproximateMaxLength, flags));

        StreamEntry[] IDatabase.StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamClaimAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags));

        RedisValue[] IDatabase.StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamClaimIdsOnlyAsync(key, consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags));

        bool IDatabase.StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamConsumerGroupSetPositionAsync(key, groupName, position, flags));

        StreamConsumerInfo[] IDatabase.StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamConsumerInfoAsync(key, groupName, flags));

        bool IDatabase.StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamCreateConsumerGroupAsync(key, groupName, position, flags));

        bool IDatabase.StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, bool createStream, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamCreateConsumerGroupAsync(key, groupName, position, createStream, flags));

        long IDatabase.StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamDeleteAsync(key, messageIds, flags));

        long IDatabase.StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamDeleteConsumerAsync(key, groupName, consumerName, flags));

        bool IDatabase.StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamDeleteConsumerGroupAsync(key, groupName, flags));

        StreamGroupInfo[] IDatabase.StreamGroupInfo(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamGroupInfoAsync(key, flags));

        StreamInfo IDatabase.StreamInfo(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamInfoAsync(key, flags));

        long IDatabase.StreamLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamLengthAsync(key, flags));

        StreamPendingInfo IDatabase.StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamPendingAsync(key, groupName, flags));

        StreamPendingMessageInfo[] IDatabase.StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId, RedisValue? maxId, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamPendingMessagesAsync(key, groupName, count, consumerName, minId, maxId, flags));

        StreamEntry[] IDatabase.StreamRange(RedisKey key, RedisValue? minId, RedisValue? maxId, int? count, Order messageOrder, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamRangeAsync(key, minId, maxId, count, messageOrder, flags));

        StreamEntry[] IDatabase.StreamRead(RedisKey key, RedisValue position, int? count, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadAsync(key, position, count, flags));

        RedisStream[] IDatabase.StreamRead(StreamPosition[] streamPositions, int? countPerStream, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadAsync(streamPositions, countPerStream, flags));

        StreamEntry[] IDatabase.StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadGroupAsync(key, groupName, consumerName, position, count, flags));

        StreamEntry[] IDatabase.StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, bool noAck, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadGroupAsync(key, groupName, consumerName, position, count, noAck, flags));

        RedisStream[] IDatabase.StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, flags));

        RedisStream[] IDatabase.StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, bool noAck, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamReadGroupAsync(streamPositions, groupName, consumerName, countPerStream, noAck, flags));

        long IDatabase.StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StreamTrimAsync(key, maxLength, useApproximateMaxLength, flags));

        long IDatabase.StringAppend(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringAppendAsync(key, value, flags));

        long IDatabase.StringBitCount(RedisKey key, long start, long end, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringBitCountAsync(key, start, end, flags));

        long IDatabase.StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringBitOperationAsync(operation, destination, first, second, flags));

        long IDatabase.StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringBitOperationAsync(operation, destination, keys, flags));

        long IDatabase.StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringBitPositionAsync(key, bit, start, end, flags));

        long IDatabase.StringDecrement(RedisKey key, long value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringDecrementAsync(key, value, flags));

        double IDatabase.StringDecrement(RedisKey key, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringDecrementAsync(key, value, flags));

        RedisValue IDatabase.StringGet(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetAsync(key, flags));

        RedisValue[] IDatabase.StringGet(RedisKey[] keys, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetAsync(keys, flags));

        bool IDatabase.StringGetBit(RedisKey key, long offset, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetBitAsync(key, offset, flags));

        Lease<byte> IDatabase.StringGetLease(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetLeaseAsync(key, flags));

        RedisValue IDatabase.StringGetRange(RedisKey key, long start, long end, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetRangeAsync(key, start, end, flags));

        RedisValue IDatabase.StringGetSet(RedisKey key, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetSetAsync(key, value, flags));

        RedisValueWithExpiry IDatabase.StringGetWithExpiry(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringGetWithExpiryAsync(key, flags));

        long IDatabase.StringIncrement(RedisKey key, long value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringIncrementAsync(key, value, flags));

        double IDatabase.StringIncrement(RedisKey key, double value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringIncrementAsync(key, value, flags));

        long IDatabase.StringLength(RedisKey key, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringLengthAsync(key, flags));

        bool IDatabase.StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringSetAsync(key, value, expiry, when, flags));

        bool IDatabase.StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringSetAsync(values, when, flags));

        bool IDatabase.StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringSetBitAsync(key, offset, bit, flags));

        RedisValue IDatabase.StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags)
            => Multiplexer.Wait(((IDatabaseAsync)this).StringSetRangeAsync(key, offset, value, flags));

    }
}
