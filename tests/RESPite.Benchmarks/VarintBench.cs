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
        if (Existing() != Proposed()) throw new InvalidOperationException("Logic error");
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
        if (BitConverter.IsLittleEndian && Sse2.IsSupported && Bmi1.IsSupported && Bmi2.IsSupported && Bmi2.X64.IsSupported)
        {
            if (span.Length >= 16)
            {
                ref byte origin = ref MemoryMarshal.GetReference(span);
                if ((origin & 0b10000000) == 0)
                {
                    value = origin;
                    return 1;
                }
                switch (Bmi1.TrailingZeroCount(~(uint)Sse2.MoveMask(Unsafe.ReadUnaligned<Vector128<byte>>(ref origin))) - 1)
                {
                    case 0:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0b01111111_01111111);
                        return 2;
                    case 1:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0b01111111_01111111_01111111);
                        return 3;
                    case 2:
                        value = Bmi2.ParallelBitExtract(Unsafe.ReadUnaligned<uint>(ref origin), 0b01111111_01111111_01111111_01111111);
                        return 4;
                    case 3:
                        value = checked((uint)Bmi2.X64.ParallelBitExtract(
                            Unsafe.ReadUnaligned<ulong>(ref origin),
                            0b01111111_01111111_01111111_01111111_01111111));
                        return 5;
                }
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
