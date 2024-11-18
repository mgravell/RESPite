using BenchmarkDotNet.Attributes;
using RESPite.Resp.Client;

namespace RESPite.Benchmarks;

public class GetBench : IntegrationBase
{
    private const int OperationsPerInvoke = 128;

    private byte[] payload = [];

    [Params(16, 64, 256, 1024)]
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
    public void SER_GetSync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            SERedis.StringGetLease(SERedisKey)?.Dispose();
        }
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public async Task SER_GetAsync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            (await SERedis.StringGetLeaseAsync(SERedisKey))?.Dispose();
        }
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_GetSync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            Strings.GET.Send(Respite, RespiteKey).Dispose();
        }
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public async Task RESP_GetAsync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            (await Strings.GET.SendAsync(Respite, RespiteKey)).Dispose();
        }
    }
}
