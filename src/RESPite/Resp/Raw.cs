using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Resp;

internal static class Raw
{
    public static ulong Create64(ReadOnlySpan<byte> bytes, int length)
    {
        if (length != bytes.Length)
        {
            throw new ArgumentException(nameof(length), $"Length check failed: {length} vs {bytes.Length}, value: {Constants.UTF8.GetString(bytes)}");
        }
        if (length < 0 || length > sizeof(ulong))
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Invalid length {length} - must be 0-{sizeof(ulong)}");
        }

        // this *will* be aligned; this approach intentionally chosen for parity with write
        Span<byte> scratch = stackalloc byte[sizeof(ulong)];
        if (length != sizeof(ulong)) scratch.Slice(length).Clear();
        bytes.CopyTo(scratch);
        return Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(scratch));
    }

    public static uint Create32(ReadOnlySpan<byte> bytes, int length)
    {
        if (length != bytes.Length)
        {
            throw new ArgumentException(nameof(length), $"Length check failed: {length} vs {bytes.Length}, value: {Constants.UTF8.GetString(bytes)}");
        }
        if (length < 0 || length > sizeof(uint))
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Invalid length {length} - must be 0-{sizeof(uint)}");
        }

        // this *will* be aligned; this approach intentionally chosen for parity with write
        Span<byte> scratch = stackalloc byte[sizeof(uint)];
        if (length != sizeof(uint)) scratch.Slice(length).Clear();
        bytes.CopyTo(scratch);
        return Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(scratch));
    }

    public static ulong BulkStringNull_5 = Create64("$-1\r\n"u8, 5);
    public static ulong BulkStringEmpty_6 = Create64("$0\r\n\r\n"u8, 6);

    public static ulong BulkStringInt32_M1_8 = Create64("$2\r\n-1\r\n"u8, 8);
    public static ulong BulkStringInt32_0_7 = Create64("$1\r\n0\r\n"u8, 7);
    public static ulong BulkStringInt32_1_7 = Create64("$1\r\n1\r\n"u8, 7);
    public static ulong BulkStringInt32_2_7 = Create64("$1\r\n2\r\n"u8, 7);
    public static ulong BulkStringInt32_3_7 = Create64("$1\r\n3\r\n"u8, 7);
    public static ulong BulkStringInt32_4_7 = Create64("$1\r\n4\r\n"u8, 7);
    public static ulong BulkStringInt32_5_7 = Create64("$1\r\n5\r\n"u8, 7);
    public static ulong BulkStringInt32_6_7 = Create64("$1\r\n6\r\n"u8, 7);
    public static ulong BulkStringInt32_7_7 = Create64("$1\r\n7\r\n"u8, 7);
    public static ulong BulkStringInt32_8_7 = Create64("$1\r\n8\r\n"u8, 7);
    public static ulong BulkStringInt32_9_7 = Create64("$1\r\n9\r\n"u8, 7);
    public static ulong BulkStringInt32_10_8 = Create64("$2\r\n10\r\n"u8, 8);

    public static ulong BulkStringPrefix_M1_5 = Create64("$-1\r\n"u8, 5);
    public static uint BulkStringPrefix_0_4 = Create32("$0\r\n"u8, 4);
    public static uint BulkStringPrefix_1_4 = Create32("$1\r\n"u8, 4);
    public static uint BulkStringPrefix_2_4 = Create32("$2\r\n"u8, 4);
    public static uint BulkStringPrefix_3_4 = Create32("$3\r\n"u8, 4);
    public static uint BulkStringPrefix_4_4 = Create32("$4\r\n"u8, 4);
    public static uint BulkStringPrefix_5_4 = Create32("$5\r\n"u8, 4);
    public static uint BulkStringPrefix_6_4 = Create32("$6\r\n"u8, 4);
    public static uint BulkStringPrefix_7_4 = Create32("$7\r\n"u8, 4);
    public static uint BulkStringPrefix_8_4 = Create32("$8\r\n"u8, 4);
    public static uint BulkStringPrefix_9_4 = Create32("$9\r\n"u8, 4);
    public static ulong BulkStringPrefix_10_5 = Create64("$10\r\n"u8, 5);

    public static ulong ArrayPrefix_M1_5 = Create64("*-1\r\n"u8, 5);
    public static uint ArrayPrefix_0_4 = Create32("*0\r\n"u8, 4);
    public static uint ArrayPrefix_1_4 = Create32("*1\r\n"u8, 4);
    public static uint ArrayPrefix_2_4 = Create32("*2\r\n"u8, 4);
    public static uint ArrayPrefix_3_4 = Create32("*3\r\n"u8, 4);
    public static uint ArrayPrefix_4_4 = Create32("*4\r\n"u8, 4);
    public static uint ArrayPrefix_5_4 = Create32("*5\r\n"u8, 4);
    public static uint ArrayPrefix_6_4 = Create32("*6\r\n"u8, 4);
    public static uint ArrayPrefix_7_4 = Create32("*7\r\n"u8, 4);
    public static uint ArrayPrefix_8_4 = Create32("*8\r\n"u8, 4);
    public static uint ArrayPrefix_9_4 = Create32("*9\r\n"u8, 4);
    public static ulong ArrayPrefix_10_5 = Create64("*10\r\n"u8, 5);
}
