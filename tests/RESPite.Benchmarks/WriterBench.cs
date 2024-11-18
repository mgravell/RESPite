﻿using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using RESPite.Resp.Writers;

namespace Benchmarks;

[MemoryDiagnoser, ShortRunJob]
public class WriterBench
{
    private const int MaxLengthPerValue = 10;
    private const int BufferSize = 2048, OperationsPerInvoke = BufferSize / MaxLengthPerValue;
    private readonly byte[] _buffer = new byte[BufferSize];

    [GlobalSetup]
    public void Setup()
    {
        // correctness check (it doesn't matter how quickly we can do the wrong thing)
        int value = Value;
        Span<byte> span = new(_buffer, 0, MaxLengthPerValue);

        span.Clear();
        RespWriter writer = new(span);
        writer.WriteBulkStringFallback(value);
        var slowOutput = writer.DebugBuffer();

        span.Clear();
        writer = new(span);
        writer.WriteBulkString(value);
        var fastOutput = writer.DebugBuffer();

        if (!slowOutput.SequenceEqual(fastOutput))
        {
            throw new InvalidOperationException($"Failure in {nameof(writer.WriteBulkString)}: '{slowOutput}' vs '{fastOutput}'");
        }

        span.Clear();
        writer = new(span);
        writer.WriteArrayFallback(value);
        slowOutput = writer.DebugBuffer();

        span.Clear();
        writer = new(span);
        writer.WriteArray(value);
        fastOutput = writer.DebugBuffer();

        if (!slowOutput.SequenceEqual(fastOutput))
        {
            throw new InvalidOperationException($"Failure in {nameof(writer.WriteArray)}: '{slowOutput}' vs '{fastOutput}'");
        }
    }

    [Params(-1, 0, 1, 2, 10, 20, 100)]
    public int Value { get; set; }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void WriteInt32Fallback()
    {
        var value = Value;
        var writer = new RespWriter(_buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            writer.WriteBulkStringFallback(value);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void WriteInt32Raw()
    {
        var value = Value;
        var writer = new RespWriter(_buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            writer.WriteBulkString(value);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void WriteArrayHeaderFallback()
    {
        var value = Value;
        var writer = new RespWriter(_buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            writer.WriteArrayFallback(value);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void WriteArrayHeaderRaw()
    {
        var value = Value;
        var writer = new RespWriter(_buffer);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            writer.WriteArray(value);
        }
    }
}
