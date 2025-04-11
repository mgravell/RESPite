using BenchmarkDotNet.Attributes;
using RESPite.Resp.Client;

namespace RESPite.Benchmarks;

public class SetBenchMultiplexed() : SetBench(true)
{
    [Params(1, 2, 10)]
    public int Threads
    {
        get => MaxDop;
        set => MaxDop = value;
    }
}
public class SetBenchReqResp() : SetBench(false) { }

public abstract class SetBench(bool multiplexed) : IntegrationBase(multiplexed)
{
    private const int OperationsPerInvoke = 128;

    private byte[] payload = [];

    [Params(16, 1024)]
    public int Length { get; set; } = 0;

    [GlobalSetup]
    public void Setup()
    {
        if (payload.Length != Length)
        {
            payload = new byte[Length];
            new Random().NextBytes(payload);
        }
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public void SER_SetSync() => Run(OperationsPerInvoke, i => SERedis.StringSet(SERedisKey, payload, TimeSpan.FromSeconds(30 * 60)));

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public Task SER_SetAsync() => RunAsync(OperationsPerInvoke, (i, ct) => new(SERedis.StringSetAsync(SERedisKey, payload, TimeSpan.FromSeconds(30 * 60))));

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_SetSync() => Run(OperationsPerInvoke, i => Strings.SETEX.Send(Respite, (RespiteKey, 30 * 60, payload)));

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public Task RESP_SetAsync() => RunAsync(OperationsPerInvoke, async (i, ct) => await Strings.SETEX.SendAsync(Respite, (RespiteKey, 30 * 60, payload)));
}
