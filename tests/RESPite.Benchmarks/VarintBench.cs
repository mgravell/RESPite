using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace RESPite.Benchmarks;

public class VarintBench
{
    [Params("01", "9601", "969601", "96969601", "9696969601")]
    public string Scenario { get; set; } = "FF";

    private byte[] value = [];

    [GlobalSetup]
    public void Setup()
    {
        value = new byte[17];
        value.AsSpan().Fill(0xFF);
        for (int i = 0; i < Scenario.Length; i += 2)
        {
            value[1 + (i / 2)] = Convert.ToByte(Scenario.Substring(i, 2), 16);
        }
        var span = value.AsSpan(1);
        var expectedLen = ParseVarintUInt32(span, out var expectedValue);

        var actualLen = VarintIntrinsics(span, out var actualValue);
        if (expectedLen != actualLen || expectedValue != actualValue)
        {
            throw new InvalidOperationException($"Logic error in {nameof(VarintIntrinsics)} {Scenario}: {expectedValue} ({expectedLen}) vs {actualValue} ({actualLen})");
        }
    }

    private const int OperationsPerInvoke = 1024;

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true)]
    public uint Existing()
    {
        var span = value.AsSpan(1);
        uint last = uint.MaxValue;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = ParseVarintUInt32(span, out last);
        }
        return last;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public uint Proposed()
    {
        var span = value.AsSpan(1);
        uint last = uint.MaxValue;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = VarintIntrinsics(span, out last);
        }
        return last;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseVarintUInt32(ReadOnlySpan<byte> span, out uint value)
    {
        value = span[0];
        return (value & 0x80) == 0 ? 1 : ParseVarintUInt32Tail(span, ref value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VarintIntrinsics(ReadOnlySpan<byte> span, out uint value)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (BitConverter.IsLittleEndian &&
            Bmi2.X64.IsSupported &&
            MemoryMarshal.TryRead(span, out ulong encoded))
        {
            if ((encoded & 0x80) == 0)
            {
                value = (byte)encoded;
                return 1;
            }

            uint highMask = Vector128.CreateScalar(encoded).AsByte().ExtractMostSignificantBits();
            uint maxMask = highMask & 0b11111; // Process 5 bytes at most
            int byteCount = BitOperations.TrailingZeroCount(~maxMask) + 1;

            // extract data from retained bytes
            ulong valueBits = Bmi2.X64.ParallelBitExtract(encoded, 0x7F_7F_7F_7F_7FU >> ((5 - byteCount) << 3));
            if (byteCount == 5)
            {
                value = checked((uint)valueBits);
            }
            else
            {
                value = (uint)valueBits;
            }
            return byteCount;
        }
#endif
        return ParseVarintUInt32(span, out value);
    }

    private static int ParseVarintUInt32Tail(ReadOnlySpan<byte> span, ref uint value)
    {
        uint chunk = span[1];
        value = (value & 0x7F) | (chunk & 0x7F) << 7;
        if ((chunk & 0x80) == 0) return 2;

        chunk = span[2];
        value |= (chunk & 0x7F) << 14;
        if ((chunk & 0x80) == 0) return 3;

        chunk = span[3];
        value |= (chunk & 0x7F) << 21;
        if ((chunk & 0x80) == 0) return 4;

        chunk = span[4];
        value |= chunk << 28; // can only use 4 bits from this chunk
        if ((chunk & 0xF0) == 0) return 5;

        ThrowOverflow();
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    private static void ThrowOverflow() => throw new OverflowException();
}
