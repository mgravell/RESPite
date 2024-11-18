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
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public abstract class IntegrationBase : IDisposable
{
    private readonly IConnectionMultiplexer multiplexer;
    protected readonly IRequestResponseTransport Respite;
    protected readonly IDatabase SERedis;

    private static readonly EndPoint ep = new IPEndPoint(IPAddress.Loopback, 6379);

    protected readonly SimpleString RespiteKey;
    protected readonly RedisKey SERedisKey;

    public IntegrationBase()
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
        Respite = conn.CreateTransport().RequestResponse(RespFrameScanner.Default);
    }

    public virtual void Dispose()
    {
        Respite.Dispose();
        multiplexer.Dispose();
    }
}
