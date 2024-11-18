using BenchmarkDotNet.Attributes;
using RESPite.Resp;
using RESPite.Resp.Client;
using RESPite.Resp.Commands;

namespace RESPite.Benchmarks;

public class IncrBench : IntegrationBase
{
    private const int OperationsPerInvoke = 128;
    private const int IncrOperationsPerInvoke = 126; // excludes DEL and GET

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public void SER_IncrSync()
    {
        SERedis.KeyDelete(SERedisKey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            SERedis.StringIncrement(SERedisKey);
        }
        int result = (int)SERedis.StringGet(SERedisKey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true, Description = "SE.Redis")]
    public async Task SER_IncrAsync()
    {
        await SERedis.KeyDeleteAsync(SERedisKey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            await SERedis.StringIncrementAsync(SERedisKey);
        }
        int result = (int)await SERedis.StringGetAsync(SERedisKey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "sync")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public void RESP_IncrSync()
    {
        Keys.DEL.Send(Respite, RespiteKey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            Strings.INCR.Send(Respite, RespiteKey);
        }
        var result = GetInt32.Send(Respite, RespiteKey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    [BenchmarkCategory("seq", "async")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "RESPite")]
    public async Task RESP_IncrAsync()
    {
        await Keys.DEL.SendAsync(Respite, RespiteKey);
        for (int i = 0; i < IncrOperationsPerInvoke; i++)
        {
            await Strings.INCR.SendAsync(Respite, RespiteKey);
        }
        var result = await GetInt32.SendAsync(Respite, RespiteKey);
        if (result != IncrOperationsPerInvoke) ThrowIncorrect(result);
    }

    private static readonly RespCommand<SimpleString, int> GetInt32 = Strings.GET.WithReader<int>(CommandFactory.Default);
    private static void ThrowIncorrect(int value) => throw new InvalidOperationException($"Unexpected result: {value}");
}
