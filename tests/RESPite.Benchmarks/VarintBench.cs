using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

#if NETCOREAPP3_0_OR_GREATER
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
        actualLen = VarintIntrinsics2(span, out actualValue);
        if (expectedLen != actualLen || expectedValue != actualValue)
        {
            throw new InvalidOperationException($"Logic error in {nameof(VarintIntrinsics2)} {Scenario}: {expectedValue} ({expectedLen}) vs {actualValue} ({actualLen})");
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

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public uint Proposed2()
    {
        var span = value.AsSpan(1);
        uint last = uint.MaxValue;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = VarintIntrinsics2(span, out last);
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
        if (BitConverter.IsLittleEndian && Sse2.IsSupported && Bmi1.IsSupported && Bmi2.IsSupported && Bmi2.X64.IsSupported)
        {
            if (span.Length >= 16)
            {
                ref byte origin = ref MemoryMarshal.GetReference(span);
                switch (Bmi1.TrailingZeroCount(~(uint)Sse2.MoveMask(Unsafe.ReadUnaligned<Vector128<byte>>(ref origin))))
                {
                    case 0:
                        value = origin;
                        return 1;
                    case 1:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0x7F7FU);
                        return 2;
                    case 2:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0x7F7F7FU);
                        return 3;
                    case 3:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0x7F7F7F7FU);
                        return 4;
                    case 4:
                        value = checked((uint)Bmi2.X64.ParallelBitExtract(Unsafe.ReadUnaligned<ulong>(ref origin), 0x7F7F7F7F7FU));
                        return 5;
                }
            }
        }
#endif
        return ParseVarintUInt32(span, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VarintIntrinsics2(ReadOnlySpan<byte> span, out uint value)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Bmi1.IsSupported && Bmi2.IsSupported && BitConverter.IsLittleEndian && span.Length >= 4)
        {
            ref byte origin = ref MemoryMarshal.GetReference(span);
            uint u32 = Unsafe.ReadUnaligned<uint>(ref origin);

            /*   input               ~|&          TZ    TZ>>3
              *   *   *  0..     *   *   *  111   0       0
              *   *  0.. 1..     *   *  111 000   8       1
              *  0.. 1.. 1..     *  111 000 000   16      2
             0.. 1.. 1.. 1..    111 000 000 000   24      3
             1.. 1.. 1.. 1..    000 000 000 000   32      4
            */
            switch (Bmi1.TrailingZeroCount(~((u32 & 0x80808080) | 0x7F7F7F7F)) >> 3)
            {
                case 0:
                    value = u32 & 0x7F;
                    return 1;
                case 1:
                    value = Bmi2.ParallelBitExtract(u32, 0x00007F7FU);
                    return 2;
                case 2:
                    value = Bmi2.ParallelBitExtract(u32, 0x007F7F7FU);
                    return 3;
                case 3:
                    value = Bmi2.ParallelBitExtract(u32, 0x7F7F7F7FU);
                    return 4;
                case 4 when span.Length >= 5:
                    ulong u64 = Bmi2.ParallelBitExtract(u32, 0x7F7F7F7FU)
                        | ((ulong)Unsafe.Add(ref origin, 4) << 28);
                    value = checked((uint)u64);
                    return 5;
            }
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
