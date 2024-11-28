using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RESPite.Resp.Readers;

public ref partial struct RespReader
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnsafeAssertClLf(int offset) => UnsafeAssertClLf(ref UnsafeCurrent, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnsafeAssertClLf(scoped ref byte source, int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)) != RespConstants.CrLfUInt16)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

    private enum LengthPrefixResult
    {
        NeedMoreData,
        Length,
        Null,
        Streaming,
    }

    /// <summary>
    /// Asserts that the current element is a scalar type.
    /// </summary>
    public readonly void DemandScalar()
    {
        if (!IsScalar) Throw();
        static void Throw() => throw new InvalidOperationException("This operation requires a scalar element");
    }

    /// <summary>
    /// Asserts that the current element is a scalar type.
    /// </summary>
    public readonly void DemandAggregate()
    {
        if (!IsAggregate) Throw();
        static void Throw() => throw new InvalidOperationException("This operation requires an aggregate element");
    }

    private static LengthPrefixResult TryReadLengthPrefix(ReadOnlySpan<byte> bytes, out int value, out int byteCount)
    {
        var end = bytes.IndexOf(RespConstants.CrlfBytes);
        if (end < 0)
        {
            byteCount = value = 0;
            if (bytes.Length >= RespConstants.MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return LengthPrefixResult.NeedMoreData;
        }
        byteCount = end + 2;
        switch (end)
        {
            case 0:
                ThrowProtocolFailure("Length prefix expected");
                goto case default; // not reached, just satisfying definite assignment
            case 1 when bytes[0] == (byte)'?':
                value = 0;
                return LengthPrefixResult.Streaming;
            default:
                if (end > RespConstants.MaxRawBytesInt32 || !(Utf8Parser.TryParse(bytes, out value, out var consumed) && consumed == end))
                {
                    ThrowProtocolFailure("Unable to parse integer");
                    value = 0;
                }
                if (value < 0)
                {
                    if (value == -1)
                    {
                        value = 0;
                        return LengthPrefixResult.Null;
                    }
                    ThrowProtocolFailure("Invalid negative length prefix");
                }
                return LengthPrefixResult.Length;
        }
    }

    // Linearize data that could be coming either from multiple segments in non-streamed RESP,
    // or from multiple streamed RESP fragments.
    private byte[] CollateScalarLeased(out int length)
    {
        Debug.Assert(IsScalar);
        byte[] arr;
        if (IsStreaming)
        {
            arr = ArrayPool<byte>.Shared.Rent(512); // take a random punt, without being excessive
            length = 0;
            while (true)
            {
                MoveNext(false);
                Debug.Assert(IsScalar);
                Debug.Assert(_length >= 0);

                if (Prefix != RespPrefix.StreamContinuation) ThrowProtocolFailure("Streaming continuation expected");

                if (_length == 0) break;
                if (arr.Length < _length + length) // need a bigger buffer
                {
                    var tmp = ArrayPool<byte>.Shared.Rent(_length + length);
                    arr.AsSpan(0, length).CopyTo(tmp);
                    ArrayPool<byte>.Shared.Return(arr);
                    arr = tmp;
                }
                UnsafeSlice(_length).CopyTo(arr.AsSpan(start: length));
                length += _length;
            }
            MovePastCurrent();
            return arr;
        }
        else
        {
            int remaining = length = _length;
            if (remaining > TotalAvailable) ThrowEOF(); // just not available
            arr = ArrayPool<byte>.Shared.Rent(remaining);
            do
            {
                var take = Math.Min(CurrentAvailable, remaining);
                UnsafeSlice(take).CopyTo(new(arr, length - remaining, take));
                remaining -= take;

                if (remaining == 0)
                {
                    return arr;
                }
            }
            while (TryMoveToNextSegment());
        }
        return ThrowEOF();

        static byte[] ThrowEOF() => throw new EndOfStreamException();
    }

    private readonly RespReader Clone() => this; // useful for performing streaming operations without moving the primary

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    private static void ThrowProtocolFailure(string message)
        => throw new InvalidOperationException("RESP protocol failure: " + message); // protocol exception?

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    internal static void ThrowEOF() => throw new EndOfStreamException();

    private int RawTryReadByte()
    {
        if (CurrentLength < _bufferIndex || TryMoveToNextSegment())
        {
            var result = UnsafeCurrent;
            _bufferIndex++;
            return result;
        }
        return -1;
    }

    private int RawPeekByte()
    {
        return CurrentLength < _bufferIndex || TryMoveToNextSegment() ? UnsafeCurrent : -1;
    }

    private bool RawAssertCrLf()
    {
        if (CurrentAvailable >= 2)
        {
            UnsafeAssertClLf(0);
            _bufferIndex += 2;
            return true;
        }
        else
        {
            int next = RawTryReadByte();
            if (next < 0) return false;
            if (next == '\r')
            {
                next = RawTryReadByte();
                if (next < 0) return false;
                if (next == '\n') return true;
            }
            ThrowProtocolFailure("Expected CR/LF");
            return false;
        }
    }

    private LengthPrefixResult RawTryReadLengthPrefix()
    {
        _length = 0;
        if (!RawTryFindCrLf(out int end))
        {
            if (TotalAvailable >= RespConstants.MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return LengthPrefixResult.NeedMoreData;
        }

        switch (end)
        {
            case 0:
                ThrowProtocolFailure("Length prefix expected");
                goto case default; // not reached, just satisfying definite assignment
            case 1 when RawPeekByte() == (byte)'?':
                _bufferIndex++;
                return LengthPrefixResult.Streaming;
            default:
                if (end > RespConstants.MaxRawBytesInt32)
                {
                    ThrowProtocolFailure("Unable to parse integer");
                }
                Span<byte> bytes = stackalloc byte[end + 2];
                RawFillBytes(bytes);
                if (!(Utf8Parser.TryParse(bytes, out _length, out var consumed) && consumed == end))
                {
                    ThrowProtocolFailure("Unable to parse integer");
                }

                if (_length < 0)
                {
                    if (_length == -1)
                    {
                        _length = 0;
                        return LengthPrefixResult.Null;
                    }
                    ThrowProtocolFailure("Invalid negative length prefix");
                }

                return LengthPrefixResult.Length;
        }
    }

    private void RawFillBytes(scoped Span<byte> target)
    {
        do
        {
            var current = CurrentSpan();
            if (current.Length >= target.Length)
            {
                // more than enough, need to trim
                current.Slice(0, target.Length).CopyTo(target);
                _bufferIndex += target.Length;
                return; // we're done
            }
            else
            {
                // take what we can
                current.CopyTo(target);
                target = target.Slice(current.Length);
                // we could move _bufferIndex here, but we're about to trash that in TryMoveToNextSegment
            }
        }
        while (TryMoveToNextSegment());
        ThrowEOF();
    }

    private readonly bool RawTryAssertCrLf()
    {
        DemandScalar();
        Debug.Assert(IsInlineScalar);
        var len = ScalarLength;

        var reader = Clone();
        if (len == 0) return reader.RawAssertCrLf();

        do
        {
            var current = reader.CurrentSpan();
            if (current.Length >= len)
            {
                reader._bufferIndex += len;
                return reader.RawAssertCrLf(); // we're done
            }
            else
            {
                // take what we can
                len -= current.Length;
                // we could move _bufferIndex here, but we're about to trash that in TryMoveToNextSegment
            }
        }
        while (reader.TryMoveToNextSegment());
        return false; // EOF
    }

    private readonly bool RawTryFindCrLf(out int length)
    {
        length = 0;
        RespReader reader = Clone();
        do
        {
            var span = reader.CurrentSpan();
            var index = span.IndexOf((byte)'\r');
            if (index >= 0)
            {
                checked
                {
                    length += index;
                }
                // move past the CR and assert the LF
                reader._bufferIndex += index + 1;
                var next = reader.RawTryReadByte();
                if (next < 0) break; // we don't know
                if (next != '\n') ThrowProtocolFailure("CR/LF expected");

                return true;
            }
            checked
            {
                length += span.Length;
            }
        }
        while (reader.TryMoveToNextSegment());
        length = 0;
        return false;
    }
}
