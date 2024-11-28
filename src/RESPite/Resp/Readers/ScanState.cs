using System.Buffers;
using System.Diagnostics;

namespace RESPite.Resp.Readers;

/// <summary>
/// Holds state used for RESP frame parsing, i.e. detecting the RESP for an entire top-level message.
/// </summary>
public struct ScanState
{
    private int _delta; // when this becomes -1, we have fully read a top-level message;
    private int _streamingAggregateDepth;
    private long _totalBytes;

    /// <summary>
    /// Gets whether an entire top-level RESP message has been consumed.
    /// </summary>
    public bool IsComplete => _delta == -1;

    /// <summary>
    /// Gets the total length of the payload read (or read so far, if it is not yet complete); this combines payloads from multiple
    /// <c>TryRead</c> operations.
    /// </summary>
    public long TotalBytes => _totalBytes;

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(ref RespReader reader, out long bytesRead)
    {
        bytesRead = ReadCore(ref reader, reader.BytesConsumed);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(ReadOnlySpan<byte> value, out int bytesRead)
    {
        var reader = new RespReader(value);
        bytesRead = (int)ReadCore(ref reader);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(in ReadOnlySequence<byte> value, out long bytesRead)
    {
        var reader = new RespReader(in value);
        bytesRead = ReadCore(ref reader);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>The number of bytes consumed in this operation.</returns>
    private long ReadCore(ref RespReader reader, long startOffset = 0)
    {
        while (_delta >= 0 && reader.TryReadNext())
        {
            if (reader.IsAggregate) ApplyAggregateRules(ref reader);

            if (_streamingAggregateDepth == 0) _delta += reader.Delta();
        }

        var bytesRead = reader.BytesConsumed - startOffset;
        _totalBytes += bytesRead;
        return bytesRead;
    }

    private void ApplyAggregateRules(ref RespReader reader)
    {
        Debug.Assert(reader.IsAggregate);
        if (reader.IsStreaming)
        {
            // entering an aggregate stream
            checked { _streamingAggregateDepth++; }
        }
        else if (reader.Prefix == RespPrefix.StreamTerminator)
        {
            // exiting an aggregate stream
            checked { _streamingAggregateDepth--; }
        }
        else if (reader.AggregateLength > 0 && _streamingAggregateDepth != 0)
        {
            ThrowNestingNotSupported();

            // The problem here is that for frame-scanning purposes, we need to know when a node is ending; if we support non-streaming inside streaming, we
            // need to know at every level whether this is a decrementing level or not, when ending nodes; at the moment, the logic is simple:
            // - if we're inside an aggregate stream, delta is zero
            // - if we're an aggregate node, delta is ChildCount - 1
            // - otherwise, delta is -1
            // Emphasis: this is doable; it is just very unlikely to ever become an issue! most likely approach here is an int32 bit mask that indicates
            // whether aggregate depth N is streaming or not.
            // Since we have the ScanState type, we can introduce this logic later as needed.
            // In particular, note that we *do not* need this complexity when reading payloads, since a: we're not trying to do incremental parse, and
            // b: when reading payloads, we are observing the hierarchy more strictly. This is purely an issue when scanning for entire frames.
            static void ThrowNestingNotSupported() => throw new NotSupportedException("Nesting non-streaming aggregates inside streaming aggregates is not currently supported by this client; please log an issue!");
        }
    }

    /// <summary>
    /// Reset this scan state to anticipate a fresh top-level RESP message.
    /// </summary>
    public void Reset() => this = default;
}
