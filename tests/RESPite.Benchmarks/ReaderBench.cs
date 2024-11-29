using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using RESPite.Resp.Readers;

namespace RESPite.Benchmarks;

[Config(typeof(CustomConfig))]
public class ReaderBench
{
    private byte[] buffer = [];

    [Params([
        "+OK\r\n",
        ":1\r\n",
        ":10\r\n",
        ":100\r\n", // not optimized
        "$-1\r\n",
        "$0\r\n\r\n",
        "$10\r\nabcdefghij\r\n",
        "*-1\r\n",
        "*0\r\n",
        "*1\r\n:0\r\n",
        "*10\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n:0\r\n",
        "-ERR nopedy nope\r\n",
        "-MOVED to somewhere\r\n", // not optimized
        ])]
    public string Scenario
    {
        get => Encoding.UTF8.GetString(buffer, 0, buffer.Length);
        set => buffer = Encoding.UTF8.GetBytes(value);
    }

    private const int OperationsPerInvoke = 1024;

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void ReadUnoptimized()
    {
        RespReader reader = new(buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            reader.DebugReset();
            int remaining = 1;
            while (remaining != 0 && reader.TryReadNextUnpotimized())
            {
                remaining = remaining + reader.ChildCount - 1;
            }
        }
        if (reader.BytesConsumed != buffer.Length) ThrowTooMuch();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void ReadOptimized()
    {
        RespReader reader = new(buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            reader.DebugReset();
            int remaining = 1;
            while (remaining != 0 && reader.TryReadNext())
            {
                remaining = remaining + reader.ChildCount - 1;
            }
        }
        if (reader.BytesConsumed != buffer.Length) ThrowTooMuch();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooMuch() => throw new InvalidOperationException("Unhandled trailing data");
}
