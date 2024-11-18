using BenchmarkDotNet.Attributes;
using RESPite.Resp.Client;

namespace RESPite.Benchmarks;

public class SetBench : IntegrationBase
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
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public void SER_SetSync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            SERedis.StringSet(SERedisKey, payload, TimeSpan.FromSeconds(30 * 60));
        }
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public async Task SER_SetAsync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            await SERedis.StringSetAsync(SERedisKey, payload, TimeSpan.FromSeconds(30 * 60));
        }
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_SetSync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            Strings.SETEX.Send(Respite, (RespiteKey, 30 * 60, payload));
        }
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public async Task RESP_SetAsync()
    {
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            await Strings.SETEX.SendAsync(Respite, (RespiteKey, 30 * 60, payload));
        }
    }
}
