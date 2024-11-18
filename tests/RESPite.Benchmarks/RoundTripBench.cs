using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using RESPite.Resp;
using RESPite.Resp.Client;
using RESPite.Resp.Commands;
using RESPite.Transports;
using StackExchange.Redis;

namespace RESPite.Benchmarks;

[MemoryDiagnoser, ShortRunJob(RuntimeMoniker.Net472), ShortRunJob(RuntimeMoniker.Net90)]
[CategoriesColumn, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class RoundTripBench
{
    private readonly IRequestResponseTransport _respite;
    private readonly IDatabase _seredis;

    private const int OperationsPerInvoke = 128;
    private const int IncrOperationsPerInvoke = 126; // excludes DEL and GET
    private const string Key = "mykey";

    private static readonly SimpleString _respitekey = Key;
    private static readonly RedisKey _serkey = Key;

    private static readonly EndPoint ep = new IPEndPoint(IPAddress.Loopback, 6379);

    public RoundTripBench()
    {
        ConfigurationOptions options = new() { EndPoints = { ep } };
        _seredis = ConnectionMultiplexer.Connect(options).GetDatabase();

        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        socket.Connect(ep);
        var conn = new NetworkStream(socket);
        _respite = conn.CreateTransport().RequestResponse(RespFrameScanner.Default);
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public void SER_IncrSync()
    {
        _seredis.KeyDelete(_serkey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            _seredis.StringIncrement(_serkey);
        }
        int result = (int)_seredis.StringGet(_serkey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public async Task SER_IncrAsync()
    {
        await _seredis.KeyDeleteAsync(_serkey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            await _seredis.StringIncrementAsync(_serkey);
        }
        int result = (int)await _seredis.StringGetAsync(_serkey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_IncrSync()
    {
        Keys.DEL.Send(_respite, _respitekey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            Strings.INCR.Send(_respite, _respitekey);
        }
        var result = GetInt32.Send(_respite, _respitekey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public async Task RESP_IncrAsync()
    {
        await Keys.DEL.SendAsync(_respite, _respitekey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            await Strings.INCR.SendAsync(_respite, _respitekey);
        }
        var result = await GetInt32.SendAsync(_respite, _respitekey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    private static readonly RespCommand<SimpleString, int> GetInt32 = Strings.GET.WithReader<int>(CommandFactory.Default);
    private static void ThrowIncorrect(int value) => throw new InvalidOperationException($"Unexpected result: {value}");
}
