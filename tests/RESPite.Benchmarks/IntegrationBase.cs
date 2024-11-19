using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using RESPite.Resp;
using RESPite.Transports;
using StackExchange.Redis;

namespace RESPite.Benchmarks;

[MemoryDiagnoser, ShortRunJob(RuntimeMoniker.Net472), ShortRunJob(RuntimeMoniker.Net90)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public abstract class IntegrationBase : IDisposable
{
    private readonly IConnectionMultiplexer multiplexer;
    protected readonly IMessageTransport Respite;
    protected readonly IDatabase SERedis;

    private static readonly EndPoint ep = new IPEndPoint(IPAddress.Loopback, 6379);

    protected readonly SimpleString RespiteKey;
    protected readonly RedisKey SERedisKey;

    public IntegrationBase(bool multiplexed)
    {
        byte[] key = Constants.UTF8.GetBytes(Guid.NewGuid().ToString());
        RespiteKey = key;
        SERedisKey = key;

        ConfigurationOptions options = new() { EndPoints = { ep } };
        multiplexer = ConnectionMultiplexer.Connect(options);
        SERedis = multiplexer.GetDatabase();

        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        socket.Connect(ep);
        var conn = new NetworkStream(socket);
        Respite = multiplexed
            ? conn.CreateTransport().WithOutboundBuffer().Multiplexed(RespFrameScanner.Default)
            : conn.CreateTransport().RequestResponse(RespFrameScanner.Default);
    }

    public virtual void Dispose()
    {
        Respite.Dispose();
        multiplexer.Dispose();
    }

    protected void Run(int operations, Action<int> action, int? maxDop = null)
    {
        var effectiveMaxDop = maxDop ?? MaxDop;
        if (effectiveMaxDop < 2)
        {
            for (int i = 0; i < operations; i++)
            {
                action(i);
            }
        }
        else
        {
            Parallel.For(0, operations, new ParallelOptions { MaxDegreeOfParallelism = effectiveMaxDop }, action);
        }
    }

    private int maxDop = 1;

    protected int MaxDop
    {
        get => maxDop;
        set => maxDop = value;
    }

    protected Task RunAsync(int operations, Func<int, CancellationToken, ValueTask> action, int? maxDop = null)
    {
        var effectiveMaxDop = maxDop ?? MaxDop;
        if (maxDop < 2)
        {
            return RunSeriallyAsync(operations, action);
        }
        else
        {
#if NET8_0_OR_GREATER
            return Parallel.ForAsync(0, operations, new ParallelOptions { MaxDegreeOfParallelism = effectiveMaxDop }, action);
#else
            return RunParallelAsync(operations, effectiveMaxDop, action);

            static Task RunParallelAsync(int operations, int maxDop, Func<int, CancellationToken, ValueTask> action)
            {
                var maxPerTask = operations / maxDop;
                List<Task> tasks = new(operations / maxPerTask);
                int start = 0;
                while (operations > 0)
                {
                    var opsThisTask = Math.Min(maxPerTask, operations);
                    var offsetThisTask = start;
                    tasks.Add(Task.Run(async () =>
                    {
                        for (int i = 0; i < opsThisTask; i++)
                        {
                            await action(offsetThisTask + i, CancellationToken.None).ConfigureAwait(false);
                        }
                    }));

                    start += opsThisTask;
                    operations -= opsThisTask;
                }
                return Task.WhenAll(tasks);
            }
#endif
        }
        static async Task RunSeriallyAsync(int operations, Func<int, CancellationToken, ValueTask> action)
        {
            for (int i = 0; i < operations; i++)
            {
                await action(i, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
