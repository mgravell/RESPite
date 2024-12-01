﻿using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
#endif

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Resp.Readers;

/// <summary>
/// Provides low level RESP parsing functionality.
/// </summary>
public ref partial struct RespReader
{
    [Flags]
    private enum RespFlags : byte
    {
        None = 0,
        IsScalar = 1 << 0, // simple strings, bulk strings, etc
        IsAggregate = 1 << 1, // arrays, maps, sets, etc
        IsNull = 1 << 2, // explicit null RESP types, or bulk-strings/aggregates with length -1
        IsInlineScalar = 1 << 3, // a non-null scalar, i.e. with payload+CrLf
        IsAttribute = 1 << 4, // is metadata for following elements
        IsStreaming = 1 << 5, // unknown length
        IsError = 1 << 6, // an explicit error reported inside the protocol
    }

    // relates to the element we're currently reading
    private RespFlags _flags;
    private RespPrefix _prefix;

    private int _length; // for null: 0; for scalars: the length of the payload; for aggregates: the child count

    // the current buffer that we're observing
    private int _bufferIndex; // after TryRead, this should be positioned immediately before the actual data

    // the position in a multi-segment payload
    private long _positionBase; // total data we've already moved past in *previous* buffers
    private ReadOnlySequenceSegment<byte>? _tail; // the next tail node
    private long _remainingTailLength; // how much more can we consume from the tail?

    private readonly int CurrentAvailable => CurrentLength - _bufferIndex;

    private readonly long TotalAvailable => CurrentAvailable + _remainingTailLength;
    private partial void UnsafeTrimCurrentBy(int count);
    private readonly partial ref byte UnsafeCurrent { get; }
    private readonly partial int CurrentLength { get; }
    private partial void SetCurrent(ReadOnlySpan<byte> value);
    private RespPrefix UnsafePeekPrefix() => (RespPrefix)UnsafeCurrent;
    private readonly partial ReadOnlySpan<byte> UnsafePastPrefix();
    private readonly partial ReadOnlySpan<byte> CurrentSpan();

    /// <summary>
    /// Get the scalar value as a single-segment span.
    /// </summary>
    /// <returns><c>True</c> if this is a non-streaming scalar element that covers a single span only, otherwise <c>False</c>.</returns>
    /// <remarks>If a scalar reports <c>False</c>, <see cref="ScalarChunks"/> can be used to iterate the entire payload.</remarks>
    /// <param name="value">When <c>True</c>, the contents of the scalar value.</param>
    public readonly bool TryGetSpan(out ReadOnlySpan<byte> value)
    {
        if (IsInlineScalar && CurrentAvailable >= _length)
        {
            value = CurrentSpan().Slice(0, _length);
            return true;
        }

        value = default;
        return IsNullScalar;
    }

    /// <summary>
    /// Returns the position after the end of the current element.
    /// </summary>
    public readonly long BytesConsumed => _positionBase + _bufferIndex + TrailingLength;

    /// <summary>
    /// Body length of scalar values, plus any terminating sentinels.
    /// </summary>
    private readonly int TrailingLength => (_flags & RespFlags.IsInlineScalar) == 0 ? 0 : (_length + 2);

    /// <summary>
    /// Gets the RESP kind of the current element.
    /// </summary>
    public readonly RespPrefix Prefix => _prefix;

    /// <summary>
    /// The payload length of this scalar element (includes combined length for streaming scalars).
    /// </summary>
    public readonly int ScalarLength() => IsInlineScalar ? _length : IsNullScalar ? 0 : checked((int)ScalarLengthSlow());

    /// <summary>
    /// Indicates whether this scalar value is zero-length.
    /// </summary>
    public readonly bool ScalarIsEmpty() => IsInlineScalar ? _length == 0 : (IsNullScalar || !ScalarChunks().MoveNext());

    /// <summary>
    /// The payload length of this scalar element (includes combined length for streaming scalars).
    /// </summary>
    public readonly long ScalarLongLength() => IsInlineScalar ? _length : IsNullScalar ? 0 : ScalarLengthSlow();

    private readonly long ScalarLengthSlow()
    {
        DemandScalar();
        long length = 0;
        var iterator = ScalarChunks();
        while (iterator.MoveNext())
        {
            length += iterator.CurrentLength;
        }
        return length;
    }

    /// <summary>
    /// The number of child elements associated with an aggregate.
    /// </summary>
    /// <remarks>For <see cref="RespPrefix.Map"/>
    /// and <see cref="RespPrefix.Attribute"/> aggregates, this is <b>twice</b> the value reported in the RESP protocol,
    /// i.e. a map of the form <c>%2\r\n...</c> will report <c>4</c> as the length.</remarks>
    /// <remarks>Note that if the data could be streaming (<see cref="IsStreaming"/>), it may be preferable to use
    /// the <see cref="AggregateChildren"/> API, using the <see cref="RespReader.AggregateEnumerator.MovePast(out RespReader)"/> API to update the outer reader.</remarks>
    public readonly int AggregateLength() => (_flags & (RespFlags.IsAggregate | RespFlags.IsStreaming)) == RespFlags.IsAggregate
            ? _length : AggregateLengthSlow();

    private readonly int AggregateLengthSlow()
    {
        DemandAggregate();
        if (!IsStreaming) return _length;
        int count = 0;
        var reader = Clone();
        while (true)
        {
            if (!reader.TryMoveNext()) ThrowEOF();
            if (reader.Prefix == RespPrefix.StreamTerminator)
            {
                return count;
            }
            reader.SkipChildren();
        }
    }

    /// <summary>
    /// Indicates whether this is a scalar value, i.e. with a potential payload body.
    /// </summary>
    public readonly bool IsScalar => (_flags & RespFlags.IsScalar) != 0;

    internal readonly bool IsInlineScalar => (_flags & RespFlags.IsInlineScalar) != 0;

    internal readonly bool IsNullScalar => (_flags & (RespFlags.IsScalar | RespFlags.IsNull)) == (RespFlags.IsScalar | RespFlags.IsNull);

    /// <summary>
    /// Indicates whether this is an aggregate value, i.e. represents a collection of sub-values.
    /// </summary>
    public readonly bool IsAggregate => (_flags & RespFlags.IsAggregate) != 0;

    /// <summary>
    /// Indicates whether this is a null value; this could be an explicit <see cref="RespPrefix.Null"/>,
    /// or a scalar or aggregate a negative reported length.
    /// </summary>
    public readonly bool IsNull => (_flags & RespFlags.IsNull) != 0;

    /// <summary>
    /// Indicates whether this is an attribute value, i.e. metadata relating to later element data.
    /// </summary>
    public readonly bool IsAttribute => (_flags & RespFlags.IsAttribute) != 0;

    /// <summary>
    /// Indicates whether this represents streaming content, where the <see cref="ScalarLength"/> or <see cref="AggregateLength"/> is not known in advance.
    /// </summary>
    public readonly bool IsStreaming => (_flags & RespFlags.IsStreaming) != 0;

    /// <summary>
    /// Equivalent to both <see cref="IsStreaming"/> and <see cref="IsAggregate"/>.
    /// </summary>
    public readonly bool IsStreamingAggregate => (_flags & (RespFlags.IsAggregate | RespFlags.IsStreaming)) == (RespFlags.IsAggregate | RespFlags.IsStreaming);

    /// <summary>
    /// Equivalent to both <see cref="IsStreaming"/> and <see cref="IsScalar"/>.
    /// </summary>
    public readonly bool IsStreamingScalar => (_flags & (RespFlags.IsScalar | RespFlags.IsStreaming)) == (RespFlags.IsScalar | RespFlags.IsStreaming);

    /// <summary>
    /// Indicates errors reported inside the protocol.
    /// </summary>
    public readonly bool IsError => (_flags & RespFlags.IsError) != 0;

    /// <summary>
    /// Gets the effective change (in terms of how many RESP nodes we expect to see) from consuming this element.
    /// For simple scalars, this is <c>-1</c> because we have one less node to read; for simple aggregates, this is
    /// <c>AggregateLength-1</c> because we will have consumed one element, but now need to read the additional
    /// <see cref="AggregateLength" /> child elements. Attributes report <c>0</c>, since they supplement data
    /// we still need to consume. The final terminator for streaming data reports a delta of <c>-1</c>, otherwise: <c>0</c>.
    /// </summary>
    /// <remarks>This does not account for being nested inside a streaming aggregate; the caller must deal with that manually.</remarks>
    internal int Delta() => (_flags & (RespFlags.IsScalar | RespFlags.IsAggregate | RespFlags.IsStreaming | RespFlags.IsAttribute)) switch
    {
        RespFlags.IsScalar => -1,
        RespFlags.IsAggregate => _length - 1,
        _ => 0,
    };

    /// <summary>
    /// Assert that this is the final element in the current payload.
    /// </summary>
    /// <exception cref="InvalidOperationException">If additional elements are available.</exception>
    public void DemandEnd()
    {
        while (IsStreamingScalar)
        {
            if (!TryReadNext()) ThrowEOF();
        }
        if (TryReadNext())
        {
            Throw(Prefix);
        }
        static void Throw(RespPrefix prefix) => throw new InvalidOperationException($"Expected end of payload, but found {prefix}");
    }

    /// <summary>
    /// Move to the next content element; this skips attribute metadata, checking for RESP error messages by default.
    /// </summary>
    /// <exception cref="EndOfStreamException">If the data is exhausted before a streaming scalar is exhausted.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    public bool TryMoveNext()
    {
        while (IsStreamingScalar) // close out the current streaming scalar
        {
            do
            {
                if (!TryReadNext()) ThrowEOF();
            }
            while (IsAttribute);
        }
        while (TryReadNext())
        {
            if (IsAttribute)
            {
                SkipChildren();
            }
            else
            {
                if (IsError) ThrowError();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Move to the next content element, asserting that it is of the expected type; this skips attribute metadata, checking for RESP error messages by default.
    /// </summary>
    /// <param name="prefix">The expected data type.</param>
    /// <exception cref="EndOfStreamException">If the data is exhausted before a streaming scalar is exhausted.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    /// <exception cref="InvalidOperationException">If the data is not of the expected type.</exception>
    public bool TryMoveNext(RespPrefix prefix)
    {
        bool result = TryMoveNext();
        if (result) Demand(prefix);
        return result;
    }

    /// <summary>
    /// Move to the next content element; this skips attribute metadata, checking for RESP error messages by default.
    /// </summary>
    /// <exception cref="EndOfStreamException">If the data is exhausted before content is found.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    public void MoveNext()
    {
        if (!TryMoveNext()) ThrowEOF();
    }

    private bool MoveNextStreamingScalar()
    {
        if (IsStreamingScalar)
        {
            while (TryReadNext())
            {
                if (IsAttribute)
                {
                    SkipChildren();
                }
                else
                {
                    if (Prefix != RespPrefix.StreamContinuation) ThrowProtocolFailure("Streaming continuation expected");
                    return _length > 0;
                }
            }
            ThrowEOF(); // we should have found something!
        }
        return false;
    }

    /// <summary>
    /// Move to the next content element (<see cref="MoveNext()"/>) and assert that it is a scalar (<see cref="DemandScalar"/>).
    /// </summary>
    /// <exception cref="EndOfStreamException">If the data is exhausted before content is found.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    /// <exception cref="InvalidOperationException">If the data is not a scalar type.</exception>
    public void MoveNextScalar()
    {
        MoveNext();
        DemandScalar();
    }

    /// <summary>
    /// Move to the next content element (<see cref="MoveNext()"/>) and assert that it is an aggregate (<see cref="DemandAggregate"/>).
    /// </summary>
    /// <exception cref="EndOfStreamException">If the data is exhausted before content is found.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    /// <exception cref="InvalidOperationException">If the data is not an aggregate type.</exception>
    public void MoveNextAggregate()
    {
        MoveNext();
        DemandScalar();
    }

    /// <summary>
    /// Move to the next content element (<see cref="MoveNext()"/>) and assert that it of type specified
    /// in <paramref name="prefix"/>.
    /// </summary>
    /// <param name="prefix">The expected data type.</param>
    /// <exception cref="EndOfStreamException">If the data is exhausted before content is found.</exception>
    /// <exception cref="RespException">If the data contains an explicit error element.</exception>
    /// <exception cref="InvalidOperationException">If the data is not of the expected type.</exception>
    public void MoveNext(RespPrefix prefix)
    {
        MoveNext();
        Demand(prefix);
    }

    private void Demand(RespPrefix prefix)
    {
        if (Prefix != prefix) Throw(prefix, Prefix);
        static void Throw(RespPrefix expected, RespPrefix actual) => throw new InvalidOperationException($"Expected {expected} element, but found {actual}.");
    }

    private readonly void ThrowError() => throw new RespException(ReadString()!);

    /// <summary>
    /// Skip all sub elements of the current node; this includes both aggregate children and scalar streaming elements.
    /// </summary>
    public void SkipChildren()
    {
        // if this is a simple non-streaming scalar, then: there's nothing to do; otherwise, re-use the
        // frame scanner logic to seek past the noise (this way, we avoid recursion etc)
        if ((_flags & (RespFlags.IsScalar | RespFlags.IsAggregate | RespFlags.IsStreaming)) != RespFlags.IsScalar)
        {
            ScanState state = new(in this);
            if (!state.TryRead(ref this, out _)) ThrowEOF();
        }
    }

    /// <summary>
    /// Reads the current element as a string value.
    /// </summary>
    public readonly string? ReadString() =>
        TryGetSpan(out var span) ? span.IsEmpty ? (IsNull ? null : "") : RespConstants.UTF8.GetString(span) : ReadStringSlow();

    private readonly string ReadStringSlow()
    {
        var length = ScalarLength();
        var oversized = ArrayPool<byte>.Shared.Rent(length);
        int actual = CopyTo(oversized);
        Debug.Assert(actual == length);
        var s = RespConstants.UTF8.GetString(oversized, 0, length);
        ArrayPool<byte>.Shared.Return(oversized);
        return s;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RespReader"/> struct.
    /// </summary>
    /// <param name="value">The raw contents to parse with this instance.</param>
    public RespReader(ReadOnlySpan<byte> value)
    {
        _length = 0;
        _flags = RespFlags.None;
        _prefix = RespPrefix.None;
        SetCurrent(value);

        _remainingTailLength = _positionBase = 0;
        _tail = null;
    }

    private void MovePastCurrent()
    {
        // skip past the trailing portion of a value, if any
        var skip = TrailingLength;
        if (_bufferIndex + skip <= CurrentLength)
        {
            _bufferIndex += skip; // available in the current buffer
        }
        else
        {
            AdvanceSlow(skip);
        }

        // reset the current state
        _length = 0;
        _flags = 0;
        _prefix = RespPrefix.None;
    }

    /// <inheritdoc cref="RespReader.RespReader(ReadOnlySpan{byte})"/>
    public RespReader(in ReadOnlySequence<byte> value)
#if NETCOREAPP3_0_OR_GREATER
        : this(value.FirstSpan)
#else
        : this(value.First.Span)
#endif
    {
        if (!value.IsSingleSegment)
        {
            _remainingTailLength = value.Length - CurrentLength;
            _tail = (value.Start.GetObject() as ReadOnlySequenceSegment<byte>)?.Next ?? MissingNext();
        }

        [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
        static ReadOnlySequenceSegment<byte> MissingNext() => throw new ArgumentException("Unable to extract tail segment", nameof(value));
    }

    /// <summary>
    /// Attempt to move to the next RESP element.
    /// </summary>
    /// <remarks>Unless you are intentionally handling errors, attributes and streaming data, <see cref="TryMoveNext()"/> should be preferred.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never), Browsable(false)]
    public unsafe bool TryReadNext()
    {
        MovePastCurrent();

#if NETCOREAPP3_0_OR_GREATER
        // check what we have available; don't worry about zero/fetching the next segment; this is only
        // for SIMD lookup, and zero would only apply when data ends exactly on segment boundaries, which
        // is incredible niche
        var available = _bufferLength - _bufferIndex;

        if (Avx2.IsSupported && Bmi1.IsSupported && available >= sizeof(uint))
        {
            // read the first 4 bytes
            ref byte origin = ref UnsafeCurrent;
            var comparand = Unsafe.ReadUnaligned<uint>(ref origin);

            // broadcast those 4 bytes into a vector, mask to get just the first and last byte, and apply a SIMD equality test with our known cases
            var eqs = Avx2.CompareEqual(Avx2.And(Avx2.BroadcastScalarToVector256(&comparand), Raw.FirstLastMask), Raw.CommonRespPrefixes);

            // reinterpret that as floats, and pick out the sign bits (which will be 1 for "equal", 0 for "not equal"); since the
            // test cases are mutually exclusive, we expect zero or one matches, so: lzcount tells us which matched
            var index = Bmi1.TrailingZeroCount((uint)Avx.MoveMask(Unsafe.As<Vector256<uint>, Vector256<float>>(ref eqs)));
            int len;
#if DEBUG
            if (VectorizeDisabled) index = uint.MaxValue; // just to break the switch
#endif
            switch (index)
            {
                case Raw.CommonRespIndex_Success when available >= 5 && Unsafe.Add(ref origin, 4) == (byte)'\n':
                    _prefix = RespPrefix.SimpleString;
                    _length = 2;
                    _bufferIndex++;
                    _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    return true;
                case Raw.CommonRespIndex_SingleDigitInteger when Unsafe.Add(ref origin, 2) == (byte)'\r':
                    _prefix = RespPrefix.Integer;
                    _length = 1;
                    _bufferIndex++;
                    _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    return true;
                case Raw.CommonRespIndex_DoubleDigitInteger when available >= 5 && Unsafe.Add(ref origin, 4) == (byte)'\n':
                    _prefix = RespPrefix.Integer;
                    _length = 2;
                    _bufferIndex++;
                    _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    return true;
                case Raw.CommonRespIndex_SingleDigitString when Unsafe.Add(ref origin, 2) == (byte)'\r':
                    if (comparand == RespConstants.BulkStringStreaming)
                    {
                        _flags = RespFlags.IsScalar | RespFlags.IsStreaming;
                    }
                    else
                    {
                        len = ParseSingleDigit(Unsafe.Add(ref origin, 1));
                        if (available < len + 6) break; // need more data

                        UnsafeAssertClLf(4 + len);
                        _length = len;
                        _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    }
                    _prefix = RespPrefix.BulkString;
                    _bufferIndex += 4;
                    return true;
                case Raw.CommonRespIndex_DoubleDigitString when available >= 5 && Unsafe.Add(ref origin, 4) == (byte)'\n':
                    if (comparand == RespConstants.BulkStringNull)
                    {
                        _length = 0;
                        _bufferIndex += 5;
                        _flags = RespFlags.IsScalar | RespFlags.IsNull;
                        return true;
                    }
                    else
                    {
                        len = ParseDoubleDigitsNonNegative(ref Unsafe.Add(ref origin, 1));
                        if (available < len + 7) break; // need more data

                        UnsafeAssertClLf(5 + len);
                        _length = len;
                        _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    }
                    _prefix = RespPrefix.BulkString;
                    _bufferIndex += 5;
                    return true;
                case Raw.CommonRespIndex_SingleDigitArray when Unsafe.Add(ref origin, 2) == (byte)'\r':
                    if (comparand == RespConstants.ArrayStreaming)
                    {
                        _flags = RespFlags.IsAggregate | RespFlags.IsStreaming;
                    }
                    else
                    {
                        _flags = RespFlags.IsAggregate;
                        _length = ParseSingleDigit(Unsafe.Add(ref origin, 1));
                    }
                    _prefix = RespPrefix.Array;
                    _bufferIndex += 4;
                    return true;
                case Raw.CommonRespIndex_DoubleDigitArray when available >= 5 && Unsafe.Add(ref origin, 4) == (byte)'\n':
                    if (comparand == RespConstants.ArrayNull)
                    {
                        _flags = RespFlags.IsAggregate | RespFlags.IsNull;
                    }
                    else
                    {
                        _length = ParseDoubleDigitsNonNegative(ref Unsafe.Add(ref origin, 1));
                        _flags = RespFlags.IsAggregate;
                    }
                    _prefix = RespPrefix.Array;
                    _bufferIndex += 5;
                    return true;
                case Raw.CommonRespIndex_Error:
                    len = UnsafePastPrefix().IndexOf(RespConstants.CrlfBytes);
                    if (len < 0) break; // need more data

                    _prefix = RespPrefix.SimpleError;
                    _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar | RespFlags.IsError;
                    _length = len;
                    _bufferIndex++;
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        static int ParseSingleDigit(byte value)
        {
            return value switch
            {
                (byte)'0' or (byte)'1' or (byte)'2' or (byte)'3' or (byte)'4' or (byte)'5' or (byte)'6' or (byte)'7' or (byte)'8' or (byte)'9' => value - (byte)'0',
                _ => Invalid(value),
            };

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static int Invalid(byte value) => throw new FormatException($"Unable to parse integer: '{(char)value}'");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ParseDoubleDigitsNonNegative(ref byte value) => (10 * ParseSingleDigit(value)) + ParseSingleDigit(Unsafe.Add(ref value, 1));
#endif

                // no fancy vectorization, but: we can still try to find the payload the fast way in a single segment
        if (_bufferIndex + 3 <= CurrentLength) // shortest possible RESP fragment is length 3
        {
            var remaining = UnsafePastPrefix();
            switch (_prefix = UnsafePeekPrefix())
            {
                case RespPrefix.SimpleString:
                case RespPrefix.SimpleError:
                case RespPrefix.Integer:
                case RespPrefix.Boolean:
                case RespPrefix.Double:
                case RespPrefix.BigNumber:
                    // CRLF-terminated
                    _length = remaining.IndexOf(RespConstants.CrlfBytes);
                    if (_length < 0) break; // can't find, need more data
                    _bufferIndex++; // payload follows prefix directly
                    _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                    if (_prefix == RespPrefix.SimpleError) _flags |= RespFlags.IsError;
                    return true;
                case RespPrefix.BulkError:
                case RespPrefix.BulkString:
                case RespPrefix.VerbatimString:
                    // length prefix with value payload; first, the length
                    switch (TryReadLengthPrefix(remaining, out _length, out int consumed))
                    {
                        case LengthPrefixResult.Length:
                            // still need to valid terminating CRLF
                            if (remaining.Length < consumed + _length + 2) break; // need more data
                            UnsafeAssertClLf(1 + consumed + _length);

                            _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                            break;
                        case LengthPrefixResult.Null:
                            _flags = RespFlags.IsScalar | RespFlags.IsNull;
                            break;
                        case LengthPrefixResult.Streaming:
                            _flags = RespFlags.IsScalar | RespFlags.IsStreaming;
                            break;
                    }
                    if (_flags == 0) break; // will need more data to know
                    if (_prefix == RespPrefix.BulkError) _flags |= RespFlags.IsError;
                    _bufferIndex += 1 + consumed;
                    return true;
                case RespPrefix.StreamContinuation:
                    // length prefix, possibly with value payload; first, the length
                    switch (TryReadLengthPrefix(remaining, out _length, out consumed))
                    {
                        case LengthPrefixResult.Length when _length == 0:
                            // EOF, no payload
                            _flags = RespFlags.IsScalar; // don't claim as streaming, we want this to count towards delta-decrement
                            break;
                        case LengthPrefixResult.Length:
                            // still need to valid terminating CRLF
                            if (remaining.Length < consumed + _length + 2) break; // need more data
                            UnsafeAssertClLf(1 + consumed + _length);

                            _flags = RespFlags.IsScalar | RespFlags.IsInlineScalar | RespFlags.IsStreaming;
                            break;
                        case LengthPrefixResult.Null:
                        case LengthPrefixResult.Streaming:
                            ThrowProtocolFailure("Invalid streaming scalar length prefix");
                            break;
                    }
                    if (_flags == 0) break; // will need more data to know
                    _bufferIndex += 1 + consumed;
                    return true;
                case RespPrefix.Array:
                case RespPrefix.Set:
                case RespPrefix.Map:
                case RespPrefix.Push:
                    // length prefix without value payload (child values follow)
                    switch (TryReadLengthPrefix(remaining, out _length, out consumed))
                    {
                        case LengthPrefixResult.Length:
                            _flags = RespFlags.IsAggregate;
                            break;
                        case LengthPrefixResult.Null:
                            _flags = RespFlags.IsAggregate | RespFlags.IsNull;
                            break;
                        case LengthPrefixResult.Streaming:
                            _flags = RespFlags.IsAggregate | RespFlags.IsStreaming;
                            break;
                    }
                    if (_flags == 0) break; // will need more data to know
                    _bufferIndex += consumed + 1;
                    return true;
                case RespPrefix.Null: // null
                    // note we already checked we had 3 bytes
                    UnsafeAssertClLf(1);
                    _flags = RespFlags.IsScalar | RespFlags.IsNull;
                    _bufferIndex += 3; // skip prefix+terminator
                    return true;
                case RespPrefix.StreamTerminator:
                    // note we already checked we had 3 bytes
                    UnsafeAssertClLf(1);
                    _flags = RespFlags.IsAggregate; // don't claim as streaming - this counts towards delta
                    return true;
                default:
                    ThrowProtocolFailure("Unexpected protocol prefix: " + _prefix);
                    return false;
            }
        }

        return TryReadNextSlow(ref this);
    }

    private static bool TryReadNextSlow(ref RespReader live)
    {
        // in the case of failure, we don't want to apply any changes,
        // so we work against an isolated copy until we're happy
        RespReader isolated = live;
        isolated.MovePastCurrent();

        int next = isolated.RawTryReadByte();
        if (next < 0) return false;

        switch (isolated._prefix = (RespPrefix)next)
        {
            case RespPrefix.SimpleString:
            case RespPrefix.SimpleError:
            case RespPrefix.Integer:
            case RespPrefix.Boolean:
            case RespPrefix.Double:
            case RespPrefix.BigNumber:
                // CRLF-terminated
                if (!isolated.RawTryFindCrLf(out isolated._length)) return false;
                isolated._flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                if (isolated._prefix == RespPrefix.SimpleError) isolated._flags |= RespFlags.IsError;
                break;
            case RespPrefix.BulkError:
            case RespPrefix.BulkString:
            case RespPrefix.VerbatimString:
                // length prefix with value payload
                switch (isolated.RawTryReadLengthPrefix())
                {
                    case LengthPrefixResult.Length:
                        // still need to valid terminating CRLF
                        if (!isolated.RawTryAssertCrLf()) return false;

                        isolated._flags = RespFlags.IsScalar | RespFlags.IsInlineScalar;
                        break;
                    case LengthPrefixResult.Null:
                        isolated._flags = RespFlags.IsScalar | RespFlags.IsNull;
                        break;
                    case LengthPrefixResult.Streaming:
                        isolated._flags = RespFlags.IsScalar | RespFlags.IsStreaming;
                        break;
                    case LengthPrefixResult.NeedMoreData:
                        return false;
                    default:
                        ThrowProtocolFailure("Unexpected length prefix");
                        return false;
                }
                if (isolated._prefix == RespPrefix.BulkError) isolated._flags |= RespFlags.IsError;
                break;
            case RespPrefix.Array:
            case RespPrefix.Set:
            case RespPrefix.Map:
            case RespPrefix.Push:
                // length prefix without value payload (child values follow)
                switch (isolated.RawTryReadLengthPrefix())
                {
                    case LengthPrefixResult.Length:
                        isolated._flags = RespFlags.IsAggregate;
                        break;
                    case LengthPrefixResult.Null:
                        isolated._flags = RespFlags.IsAggregate | RespFlags.IsNull;
                        break;
                    case LengthPrefixResult.Streaming:
                        isolated._flags = RespFlags.IsAggregate | RespFlags.IsStreaming;
                        break;
                    case LengthPrefixResult.NeedMoreData:
                        return false;
                    default:
                        ThrowProtocolFailure("Unexpected length prefix");
                        return false;
                }
                break;
            case RespPrefix.Null: // null
                if (!isolated.RawAssertCrLf()) return false;
                isolated._flags = RespFlags.IsScalar | RespFlags.IsNull;
                break;
            case RespPrefix.StreamTerminator:
                if (!isolated.RawAssertCrLf()) return false;
                isolated._flags = RespFlags.IsAggregate; // don't claim as streaming - this counts towards delta
                break;
            case RespPrefix.StreamContinuation:
                // length prefix, possibly with value payload; first, the length
                switch (isolated.RawTryReadLengthPrefix())
                {
                    case LengthPrefixResult.Length when isolated._length == 0:
                        // EOF, no payload
                        isolated._flags = RespFlags.IsScalar; // don't claim as streaming, we want this to count towards delta-decrement
                        break;
                    case LengthPrefixResult.Length:
                        // still need to valid terminating CRLF
                        if (!isolated.RawTryAssertCrLf()) return false; // need more data

                        isolated._flags = RespFlags.IsScalar | RespFlags.IsInlineScalar | RespFlags.IsStreaming;
                        break;
                    case LengthPrefixResult.Null:
                    case LengthPrefixResult.Streaming:
                        ThrowProtocolFailure("Invalid streaming scalar length prefix");
                        break;
                    case LengthPrefixResult.NeedMoreData:
                    default:
                        return false;
                }
                break;
            default:
                ThrowProtocolFailure("Unexpected protocol prefix: " + isolated._prefix);
                return false;
        }
        // commit the speculative changes back, and accept
        live = isolated;
        return true;
    }

    private void AdvanceSlow(long bytes)
    {
        while (bytes > 0)
        {
            var available = CurrentLength - _bufferIndex;
            if (bytes <= available)
            {
                _bufferIndex += (int)bytes;
                return;
            }
            bytes -= available;

            if (!TryMoveToNextSegment()) Throw();
        }

        [DoesNotReturn]
        static void Throw() => throw new EndOfStreamException("Unexpected end of payload; this is unexpected because we already validated that it was available!");
    }

    private bool TryMoveToNextSegment()
    {
        while (_tail is not null)
        {
            var memory = _tail.Memory;
            _tail = _tail.Next;
            if (!memory.IsEmpty)
            {
                var span = memory.Span; // check we can get this before mutating anything
                _positionBase += CurrentLength;
                _remainingTailLength -= span.Length;
                SetCurrent(span);
                return true;
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool IsOK() // go mad with this, because it is used so often
    {
        return TryGetSpan(out var span) && span.Length == 2
                ? Unsafe.ReadUnaligned<ushort>(ref UnsafeCurrent) == RespConstants.OKUInt16
                : IsSlow(RespConstants.OKBytes);
    }

    /// <summary>
    /// Indicates whether the current element is a scalar with a value that matches the provided <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The payload value to verify.</param>
    public readonly bool Is(ReadOnlySpan<byte> value)
        => TryGetSpan(out var span) ? span.SequenceEqual(value) : IsSlow(value);

    /// <summary>
    /// Indicates whether the current element is a scalar with a value that matches the provided <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The payload value to verify.</param>
    public readonly bool Is(byte value)
    {
        if (IsInlineScalar && _length == 1 && CurrentAvailable >= 1)
        {
            return UnsafeCurrent == value;
        }

        ReadOnlySpan<byte> span = [value];
        return IsSlow(span);
    }

    private readonly bool IsSlow(ReadOnlySpan<byte> testValue)
    {
        DemandScalar();
        if (IsNull) return false; // nothing equals null
        if (TotalAvailable < testValue.Length) return false;

        var iterator = ScalarChunks();
        while (true)
        {
            if (testValue.IsEmpty)
            {
                // nothing left to test; if also nothing left to read, great!
                return !iterator.MoveNext();
            }
            if (!iterator.MoveNext())
            {
                return false; // test is longer
            }

            var current = iterator.Current;
            if (testValue.Length < current.Length) return false; // payload is longer

            if (!current.SequenceEqual(testValue.Slice(0, current.Length))) return false; // payload is different

            testValue = testValue.Slice(current.Length); // validated; continue
        }
    }

    /// <summary>
    /// Copy the current scalar value out into the supplied <paramref name="target"/>, or as much as can be copied.
    /// </summary>
    /// <param name="target">The destination for the copy operation.</param>
    /// <returns>The number of bytes successfully copied.</returns>
    public readonly int CopyTo(Span<byte> target)
    {
        if (TryGetSpan(out var value))
        {
            if (target.Length < value.Length) value = value.Slice(0, target.Length);

            value.CopyTo(target);
            return value.Length;
        }

        int totalBytes = 0;
        var iterator = ScalarChunks();
        while (iterator.MoveNext())
        {
            value = iterator.Current;
            if (target.Length <= value.Length)
            {
                value.Slice(0, target.Length).CopyTo(target);
                return totalBytes + target.Length;
            }

            value.CopyTo(target);
            target = target.Slice(value.Length);
            totalBytes += value.Length;
        }
        return totalBytes;
    }

    /// <summary>
    /// Asserts that the current element is not null.
    /// </summary>
    public void DemandNotNull()
    {
        if (IsNull) Throw();
        static void Throw() => throw new InvalidOperationException("A non-null element was expected");
    }

    /// <summary>
    /// Read the current element as a <see cref="long"/> value.
    /// </summary>
    public readonly long ReadInt64()
    {
        if (TryGetSpan(out var span) && span.Length <= RespConstants.MaxRawBytesInt64)
        {
            if (!(Utf8Parser.TryParse(span, out long value, out int bytes) & bytes == _length))
            {
                ThrowFormatException();
            }
            return value;
        }
        return ReadInt64Slow();
    }

    private readonly long ReadInt64Slow()
    {
        DemandScalar();
        Span<byte> oversized = stackalloc byte[RespConstants.MaxRawBytesInt64 + 1];
        int len = CopyTo(oversized);
        if (!(len <= RespConstants.MaxRawBytesInt64 && Utf8Parser.TryParse(oversized.Slice(0, len), out long value, out int bytes) & bytes == len))
        {
            ThrowFormatException();
            return 0;
        }
        return value;
    }

    /// <summary>
    /// Read the current element as a <see cref="int"/> value.
    /// </summary>
    public readonly int ReadInt32()
    {
        if (TryGetSpan(out var span) && span.Length <= RespConstants.MaxRawBytesInt32)
        {
            if (!(Utf8Parser.TryParse(span, out int value, out int bytes) & bytes == _length))
            {
                ThrowFormatException();
            }
            return value;
        }
        return ReadInt32Slow();
    }

    private readonly int ReadInt32Slow()
    {
        DemandScalar();
        Span<byte> oversized = stackalloc byte[RespConstants.MaxRawBytesInt32 + 1];
        int len = CopyTo(oversized);
        if (!(len <= RespConstants.MaxRawBytesInt32 && Utf8Parser.TryParse(oversized.Slice(0, len), out int value, out int bytes) & bytes == len))
        {
            ThrowFormatException();
            return 0;
        }
        return value;
    }

    /// <summary>
    /// Parse a scalar value as an enum of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="unknownValue">The value to report if the value is not recognized.</param>
    public readonly T ReadEnum<T>(T unknownValue = default) where T : struct, Enum
    {
        DemandScalar();

#if NET6_0_OR_GREATER
        const int MAX_STACK = 128;
        T value;
        if (TryGetSpan(out var bytes) && bytes.Length <= MAX_STACK)
        {
            Span<char> chars = stackalloc char[RespConstants.UTF8.GetMaxCharCount(bytes.Length)];
            var count = RespConstants.UTF8.GetChars(bytes, chars);
            if (!Enum.TryParse(chars.Slice(0, count), true, out value))
            {
                value = unknownValue;
            }
        }
        else
        {
            var byteLength = ScalarLength();
            var byteBuffer = ArrayPool<byte>.Shared.Rent(byteLength);
            var actual = CopyTo(byteBuffer);
            Debug.Assert(actual == byteLength);
            var charBuffer = ArrayPool<char>.Shared.Rent(RespConstants.UTF8.GetMaxCharCount(byteLength));
            var charLength = RespConstants.UTF8.GetChars(byteBuffer, 0, byteLength, charBuffer, 0);
            if (!Enum.TryParse(new ReadOnlySpan<char>(charBuffer, 0, charLength), true, out value))
            {
                value = unknownValue;
            }
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
        return value;
#else
        if (!Enum.TryParse(ReadString(), true, out T value))
        {
            value = unknownValue;
        }
        return value;
#endif
    }
}
