using PooledAwait;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Respite.Internal
{
    internal static class Lowfx
    {
#if LOWFX
        public unsafe static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* c = chars)
            fixed (byte* b = bytes)
            {
                return encoding.GetBytes(c, chars.Length, b, bytes.Length);
            }
        }
        public unsafe static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> chars)
        {
            fixed (char* c = chars)
            {
                return encoding.GetByteCount(c, chars.Length);
            }
        }
        public unsafe static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* b = bytes)
            {
                return encoding.GetString(b, bytes.Length);
            }
        }

        public unsafe static void Convert(this Encoder encoder, ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            fixed (char* c = chars)
            fixed (byte* b = bytes)
            {
                encoder.Convert(c, chars.Length, b, bytes.Length, flush, out charsUsed, out bytesUsed, out completed);
            }
        }

        public static ValueTask DisposeAsync(this Stream stream)
        {
            stream.Dispose();
            return default;
        }

        public unsafe static string CreateString(this ReadOnlySpan<char> chars)
        {
            fixed (char* c = chars)
            {
                return new string(c, 0, chars.Length);
            }
        }

        public static int Read(this Stream stream, Span<byte> buffer)
        {
            var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int bytes = stream.Read(arr, 0, buffer.Length);
                if (bytes != 0) new ReadOnlySpan<byte>(arr, 0, bytes).CopyTo(buffer);
                return bytes;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
        }
        public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var pending = stream.ReadAsync(arr, 0, buffer.Length, cancellationToken);
                if (pending.Status != TaskStatus.RanToCompletion) return Awaited(arr, buffer, pending);

                var bytes = Complete(arr, buffer, pending.Result);
                return new ValueTask<int>(bytes);
            }
            catch
            {
                Complete(arr, buffer, 0);
                throw;
            }
            static int Complete(byte[] source, Memory<byte> destination, int bytes)
            {
                if (bytes > 0) new ReadOnlySpan<byte>(source, 0, bytes).CopyTo(destination.Span);
                ArrayPool<byte>.Shared.Return(source);
                return bytes;
            }

            static async PooledValueTask<int> Awaited(byte[] arr, Memory<byte> buffer, Task<int> pending)
            {
                try
                {
                    var bytes = await pending.ConfigureAwait(false);
                    return Complete(arr, buffer, bytes);
                }
                catch
                {
                    Complete(arr, buffer, 0);
                    throw;
                }
            }
        }

        public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
        {
            var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(arr);
                stream.Write(arr, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(arr);
            }
        }
        public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(arr);
                var pending = stream.WriteAsync(arr, 0, buffer.Length, cancellationToken);
                if (pending.Status != TaskStatus.RanToCompletion) return Awaited(arr, pending);
                Return(arr);
                return default;
            }
            catch
            {
                Return(arr);
                throw;
            }
            static void Return(byte[] arr) => ArrayPool<byte>.Shared.Return(arr);
            static async PooledValueTask Awaited(byte[] arr, Task pending)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                finally
                {
                    Return(arr);
                }
            }
        }
#else
        public static string CreateString(this ReadOnlySpan<char> chars) => new string(chars);
#endif
    }
}