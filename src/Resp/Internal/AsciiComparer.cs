using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Resp.Internal
{
    internal static class AsciiComparer
    {
        private const byte BYTE_DELTA = (byte)('a' - 'A');

        private static readonly Vector<byte> s_A = new Vector<byte>((byte)'A'), s_Z = new Vector<byte>((byte)'Z'), s_Delta = new Vector<byte>(BYTE_DELTA);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualCaseInsensitive(in ReadOnlySequence<byte> x, in ReadOnlySequence<byte> y)
            => (x.IsSingleSegment & x.IsSingleSegment) ? EqualCaseInsensitive(x.FirstSpan, y.FirstSpan)
                : SlowEqualCaseInsensitive(x, y);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SlowEqualCaseInsensitive(ReadOnlySequence<byte> x, ReadOnlySequence<byte> y)
        {
            if (x.Length != y.Length) return false;
            while (!x.IsEmpty)
            {
                ReadOnlySpan<byte> a = x.FirstSpan, b = y.FirstSpan;
                var take = Math.Min(a.Length, b.Length);
                if (take == 0) ThrowHelper.Invalid("math is hard");

                if (!EqualCaseInsensitive(a.Slice(0, take), b.Slice(0, take))) return false;
                x = x.Slice(take);
                y = y.Slice(take);
            }
            return true;
        }

        public static bool EqualCaseInsensitive(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
        {
            if (x.Length != y.Length) return false;
            int index = 0;
            if (Vector.IsHardwareAccelerated)
            {
                if (x.Length >= Vector<byte>.Count)
                {
                    var xV = MemoryMarshal.Cast<byte, Vector<byte>>(x);
                    var yV = MemoryMarshal.Cast<byte, Vector<byte>>(y);
                    for (int i = 0; i < xV.Length; i++)
                    {
                        // hope for SIMD-width equality
                        if (xV[i] != yV[i] && ToLowerVector(in xV[i]) != ToLowerVector(in yV[i])) return false;
                    }
                    index = xV.Length * Vector<byte>.Count;
                }
            }
            else
            {
                if (x.Length >= sizeof(ulong))
                {
                    var xV = MemoryMarshal.Cast<byte, ulong>(x);
                    var yV = MemoryMarshal.Cast<byte, ulong>(y);
                    for (int i = 0; i < xV.Length; i++)
                    {
                        if (xV[i] != yV[i]) // hope for qword equality
                        {
                            int offset = i * sizeof(ulong);
                            if (ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])
                                | ToLowerScalar(x[offset]) != ToLowerScalar(y[offset++])) return false;
                        }
                    }
                    index = xV.Length * sizeof(ulong);
                }
            }
            for (; index < x.Length; index++)
            {
                if (ToLowerScalar(x[index]) != ToLowerScalar(y[index])) return false;
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte ToLowerScalar(byte value) => value >= (byte)'A' & value <= (byte)'Z' ? (byte)(value | BYTE_DELTA) : value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<byte> ToLowerVector(in Vector<byte> value) =>
            Vector.ConditionalSelect(
                Vector.GreaterThanOrEqual(value, s_A) & Vector.LessThanOrEqual(value, s_Z),
                value | s_Delta, value);
    }
}
