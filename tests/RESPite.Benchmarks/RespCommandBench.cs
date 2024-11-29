#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace RESPite.Benchmarks;

public class RespCommandBench
{
    [Params([
        "*1\r\n$4\r\nPING\r\n",
        "*2\r\n$3\r\nGET\r\n$3\r\nfoo\r\n",
        "*3\r\n$3\r\nSET\r\n$3\r\nfoo\r\n$3\r\nbar\r\n",
        "*2\r\n$3\r\nNIL\r\n$3\r\nfoo\r\n",
        "*2\r\n$6\r\nZSCORE\r\n$3\r\nfoo\r\n",
    ])]
    public string Scenario { get; set; } = "";

    private byte[] payload = [];

    [GlobalSetup]
    public void Setup()
    {
        payload = Encoding.ASCII.GetBytes(Scenario + "trailing gibberish");

        if (ViaIndexOf() != ViaAvx2()) throw new InvalidOperationException($"Logic error: {ViaIndexOf()} vs {ViaAvx2()}");
    }

    private const int OperationsPerInvoke = 1024;

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true)]
    public RedisCommand ViaIndexOf()
    {
        RedisCommand last = default;
        ReadOnlySpan<byte> span = payload;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            last = FindEnding(span, out _, out _, out _);
        }
        return last;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand ViaAvx2()
    {
        RedisCommand last = default;
        ReadOnlySpan<byte> span = payload;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            last = FindEndingAvx2(span, out _, out _, out _);
        }
        return last;
    }

    private static readonly ulong Filter_SingleDigitArgSingleDigitCmdLen = MemoryMarshal.Read<ulong>("*\0\0\n$\0\0\n"u8);
    private static readonly ulong Filter_SingleDigitArgDoubleDigitCmdLen = MemoryMarshal.Read<ulong>("*\0\0\n$\0\0\r"u8);
    private static readonly ulong Filter_DoubleDigitArgSingleDigitCmdLen = MemoryMarshal.Read<ulong>("*\0\0\r\n\0\0\r"u8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Digit(byte value)
    {
        return value >= '0' && value <= '9' ? (value - '0') : Throw();

        [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
        static int Throw() => throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static RedisCommand FindEnding(ReadOnlySpan<byte> argCountAndCommand, out int argCount, out int commandLength, out int preambleLength)
    {
        if (TryGetCommandParts(argCountAndCommand, out argCount, out commandLength, out preambleLength)
            && commandLength <= MAX_COMMAND_LENGTH)
        {
            ReadOnlySpan<uint> candidates = __endings[commandLength];

            var lastFour = MemoryMarshal.Read<uint>(argCountAndCommand.Slice(preambleLength - 6, 4));
            var index = candidates.IndexOf(lastFour);
            if (index >= 0) return __commands[commandLength][index];
        }
        return RedisCommand.NONE;
    }

    private static RedisCommand FindEndingAvx2(ReadOnlySpan<byte> argCountAndCommand, out int argCount, out int commandLength, out int preambleLength)
    {
        if (TryGetCommandParts(argCountAndCommand, out argCount, out commandLength, out preambleLength)
            && commandLength <= MAX_COMMAND_LENGTH)
        {
            ReadOnlySpan<uint> candidates = __endings[commandLength];

            var lastFour = MemoryMarshal.Read<uint>(argCountAndCommand.Slice(preambleLength - 6, 4));
            var index = Avx2IndexOf(candidates, lastFour);
            if (index >= 0) return __commands[commandLength][index];
        }
        return RedisCommand.NONE;
    }

    private static int Avx2IndexOf(ReadOnlySpan<uint> input, uint value)
    {
        int length = input.Length, offset = 0;
        ref uint origin = ref MemoryMarshal.GetReference(input);

        if (Vector512.IsHardwareAccelerated && offset + Vector512<uint>.Count <= length)
        {
            var findVector = Vector512.Create(value);
            do
            {
                var eqs = Vector512.Equals<uint>(Vector512.LoadUnsafe<uint>(ref Unsafe.Add(ref origin, offset)), findVector);
                if (eqs != Vector512<uint>.Zero)
                {
                    var found = BitOperations.TrailingZeroCount(eqs.ExtractMostSignificantBits());
                    if (found < Vector512<uint>.Count)
                    {
                        return offset + found;
                    }
                }

                offset += Vector512<uint>.Count;
            }
            while (offset + Vector512<uint>.Count <= length);
        }

        if (Vector256.IsHardwareAccelerated && offset + Vector256<uint>.Count <= length)
        {
            var findVector = Vector256.Create(value);
            do
            {
                var eqs = Vector256.Equals<uint>(Vector256.LoadUnsafe<uint>(ref Unsafe.Add(ref origin, offset)), findVector);
                if (eqs != Vector256<uint>.Zero)
                {
                    var found = BitOperations.TrailingZeroCount(eqs.ExtractMostSignificantBits());
                    if (found < Vector256<uint>.Count)
                    {
                        return offset + found;
                    }
                }

                offset += Vector256<uint>.Count;
            }
            while (offset + Vector256<uint>.Count <= length);
        }

        input = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref origin, offset), input.Length - offset);
        var foundAt = input.IndexOf(value);
        return foundAt < 0 ? -1 : (foundAt + offset);
    }

    private static bool TryGetCommandParts(ReadOnlySpan<byte> argCountAndCommand, out int argCount, out int commandLength, out int preambleLength)
    {
        var len = argCountAndCommand.Length;
        argCount = commandLength = preambleLength = 0;
        if (len < 12) return false; // minimal sensible 2-char command: *1\r\n$2\r\nXX\r\n

        ref byte origin = ref MemoryMarshal.GetReference(argCountAndCommand);

        var preamble = Unsafe.ReadUnaligned<ulong>(ref origin);
        var filtered = preamble & 0xFF0000FFFF0000FF;

        if (filtered == Filter_SingleDigitArgSingleDigitCmdLen)
        {
            if (Unsafe.Add(ref origin, 2) == '\r' && Unsafe.Add(ref origin, 6) == '\r')
            {
                argCount = Digit(Unsafe.Add(ref origin, 1));
                commandLength = Digit(Unsafe.Add(ref origin, 5));
                preambleLength = 10 + commandLength;
                return len >= preambleLength;
            }
        }
        else if (filtered == Filter_SingleDigitArgDoubleDigitCmdLen)
        {
            if (Unsafe.Add(ref origin, 2) == '\r' && Unsafe.Add(ref origin, 8) == '\n')
            {
                argCount = Digit(Unsafe.Add(ref origin, 1));
                commandLength = (10 & Digit(Unsafe.Add(ref origin, 5))) + Digit(Unsafe.Add(ref origin, 6));
                preambleLength = 11 + commandLength;
                return len >= preambleLength;
            }
        }
        else if (filtered == Filter_DoubleDigitArgSingleDigitCmdLen)
        {
            if (Unsafe.Add(ref origin, 8) == '\n')
            {
                argCount = (10 & Digit(Unsafe.Add(ref origin, 1))) + Digit(Unsafe.Add(ref origin, 2));
                commandLength = Digit(Unsafe.Add(ref origin, 6));
                preambleLength = 11 + commandLength;
                return len >= preambleLength;
            }
        }
        return false;
    }

    private const int MAX_COMMAND_LENGTH = 10;
    private static readonly RedisCommand[][] __commands;
    private static readonly uint[][] __endings = BuildEndings(out __commands);

    private static uint[][] BuildEndings(out RedisCommand[][] commands)
    {
        int maxLen = MAX_COMMAND_LENGTH;
        var values = (RedisCommand[])Enum.GetValues(typeof(RedisCommand));
        var names = Enum.GetNames(typeof(RedisCommand));

        uint[][] result = new uint[maxLen + 1][];
        commands = new RedisCommand[maxLen + 1][];
        result[0] = result[1] = [];
        commands[0] = commands[1] = [];

        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        for (int cmdLen = 2; cmdLen <= maxLen; cmdLen++)
        {
            var count = 0;
            for (int j = 0; j < names.Length; j++)
            {
                if (names[j].Length == cmdLen) count++;
            }
            if (count == 0)
            {
                result[cmdLen] = [];
                commands[cmdLen] = [];
                continue;
            }
            var lenValues = result[cmdLen] = new uint[count];
            var lenCommands = commands[cmdLen] = new RedisCommand[count];

            int take = Math.Min(cmdLen, buffer.Length);
            var scratch = buffer.Slice(buffer.Length - take);
            switch (take)
            {
                case 2:
                    buffer[0] = (byte)'\r';
                    buffer[1] = (byte)'\n';
                    break;
                case 3:
                    buffer[0] = (byte)'\n';
                    break;
            }
            int index = 0;
            for (int j = 0; j < names.Length; j++)
            {
                if (names[j].Length == cmdLen)
                {
                    var bytes = Encoding.ASCII.GetBytes(names[j].AsSpan(cmdLen - take, take), scratch);
                    Debug.Assert(bytes == take);
                    lenValues[index] = Unsafe.ReadUnaligned<uint>(ref buffer[0]);
                    lenCommands[index] = values[j];
                    index++;
                }
            }
            Debug.Assert(index == count);
        }
        return result;
    }
}

public enum RedisCommand // should probably be ordered by frequency
{
    NONE, // must be first for "zero reasons"

    APPEND,
    ASKING,
    AUTH,

    BGREWRITEAOF,
    BGSAVE,
    BITCOUNT,
    BITOP,
    BITPOS,
    BLPOP,
    BRPOP,
    BRPOPLPUSH,

    CLIENT,
    CLUSTER,
    CONFIG,
    COPY,
    COMMAND,

    DBSIZE,
    DEBUG,
    DECR,
    DECRBY,
    DEL,
    DISCARD,
    DUMP,

    ECHO,
    EVAL,
    EVALSHA,
    EVAL_RO,
    EVALSHA_RO,
    EXEC,
    EXISTS,
    EXPIRE,
    EXPIREAT,
    EXPIRETIME,

    FLUSHALL,
    FLUSHDB,

    GEOADD,
    GEODIST,
    GEOHASH,
    GEOPOS,
    GEORADIUS,
    GEORADIUSBYMEMBER,
    GEOSEARCH,
    GEOSEARCHSTORE,

    GET,
    GETBIT,
    GETDEL,
    GETEX,
    GETRANGE,
    GETSET,

    HDEL,
    HELLO,
    HEXISTS,
    HEXPIRE,
    HEXPIREAT,
    HEXPIRETIME,
    HGET,
    HGETALL,
    HINCRBY,
    HINCRBYFLOAT,
    HKEYS,
    HLEN,
    HMGET,
    HMSET,
    HPERSIST,
    HPEXPIRE,
    HPEXPIREAT,
    HPEXPIRETIME,
    HPTTL,
    HRANDFIELD,
    HSCAN,
    HSET,
    HSETNX,
    HSTRLEN,
    HVALS,

    INCR,
    INCRBY,
    INCRBYFLOAT,
    INFO,

    KEYS,

    LASTSAVE,
    LATENCY,
    LCS,
    LINDEX,
    LINSERT,
    LLEN,
    LMOVE,
    LMPOP,
    LPOP,
    LPOS,
    LPUSH,
    LPUSHX,
    LRANGE,
    LREM,
    LSET,
    LTRIM,

    MEMORY,
    MGET,
    MIGRATE,
    MONITOR,
    MOVE,
    MSET,
    MSETNX,
    MULTI,

    OBJECT,

    PERSIST,
    PEXPIRE,
    PEXPIREAT,
    PEXPIRETIME,
    PFADD,
    PFCOUNT,
    PFMERGE,
    PING,
    PSETEX,
    PSUBSCRIBE,
    PTTL,
    PUBLISH,
    PUBSUB,
    PUNSUBSCRIBE,

    QUIT,

    RANDOMKEY,
    READONLY,
    READWRITE,
    RENAME,
    RENAMENX,
    REPLICAOF,
    RESTORE,
    ROLE,
    RPOP,
    RPOPLPUSH,
    RPUSH,
    RPUSHX,

    SADD,
    SAVE,
    SCAN,
    SCARD,
    SCRIPT,
    SDIFF,
    SDIFFSTORE,
    SELECT,
    SENTINEL,
    SET,
    SETBIT,
    SETEX,
    SETNX,
    SETRANGE,
    SHUTDOWN,
    SINTER,
    SINTERCARD,
    SINTERSTORE,
    SISMEMBER,
    SLAVEOF,
    SLOWLOG,
    SMEMBERS,
    SMISMEMBER,
    SMOVE,
    SORT,
    SORT_RO,
    SPOP,
    SRANDMEMBER,
    SREM,
    STRLEN,
    SUBSCRIBE,
    SUNION,
    SUNIONSTORE,
    SSCAN,
    SWAPDB,
    SYNC,

    TIME,
    TOUCH,
    TTL,
    TYPE,

    UNLINK,
    UNSUBSCRIBE,
    UNWATCH,

    WATCH,

    XACK,
    XADD,
    XAUTOCLAIM,
    XCLAIM,
    XDEL,
    XGROUP,
    XINFO,
    XLEN,
    XPENDING,
    XRANGE,
    XREAD,
    XREADGROUP,
    XREVRANGE,
    XTRIM,

    ZADD,
    ZCARD,
    ZCOUNT,
    ZDIFF,
    ZDIFFSTORE,
    ZINCRBY,
    ZINTER,
    ZINTERCARD,
    ZINTERSTORE,
    ZLEXCOUNT,
    ZMPOP,
    ZMSCORE,
    ZPOPMAX,
    ZPOPMIN,
    ZRANDMEMBER,
    ZRANGE,
    ZRANGEBYLEX,
    ZRANGEBYSCORE,
    ZRANGESTORE,
    ZRANK,
    ZREM,
    ZREMRANGEBYLEX,
    ZREMRANGEBYRANK,
    ZREMRANGEBYSCORE,
    ZREVRANGE,
    ZREVRANGEBYLEX,
    ZREVRANGEBYSCORE,
    ZREVRANK,
    ZSCAN,
    ZSCORE,
    ZUNION,
    ZUNIONSTORE,
}
#endif
