using BenchmarkDotNet.Attributes;
using RESPite.Resp.Client;

namespace RESPite.Benchmarks;

public class GetBenchMultiplexed() : GetBench(true)
{
    [Params(1, 2, 10)]
    public int Threads
    {
        get => MaxDop;
        set => MaxDop = value;
    }
}

public class GetBenchReqResp() : GetBench(false) { }

public abstract class GetBench(bool multiplexed) : IntegrationBase(multiplexed)
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
        SERedis.StringSet(SERedisKey, payload, TimeSpan.FromMinutes(30));
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public void SER_GetSync() => Run(OperationsPerInvoke, i => SERedis.StringGetLease(SERedisKey)?.Dispose());

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public Task SER_GetAsync() => RunAsync(OperationsPerInvoke, async (i, ct) => (await SERedis.StringGetLeaseAsync(SERedisKey))?.Dispose());

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_GetSync() => Run(OperationsPerInvoke, i => Strings.GET.Send(Respite, RespiteKey).Dispose());

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public Task RESP_GetAsync() => RunAsync(OperationsPerInvoke, async (i, ct) => (await Strings.GET.SendAsync(Respite, RespiteKey)).Dispose());
}
