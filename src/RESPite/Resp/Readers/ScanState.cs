using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Resp.Readers;

/// <summary>
/// Holds state used for RESP frame parsing, i.e. detecting the RESP for an entire top-level message.
/// </summary>
public struct ScanState
{
    private int _delta; // when this becomes -1, we have fully read a top-level message;
    private ushort _streamingAggregateDepth;
    private MessageKind _kind;
    private long _totalBytes;
#if DEBUG
    private int _elementCount;

    /// <inheritdoc/>
    public override string ToString() => $"{_kind}, consumed: {_totalBytes} bytes, {_elementCount} nodes, complete: {IsComplete}";
#else
    /// <inheritdoc/>
    public override string ToString() => nameof(ScanState);
#endif

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    private enum MessageKind : byte
    {
        Root, // we haven't yet seen the first non-attribute element
        PubSubRoot, // we haven't yet seen the first non-attribute element, and this is a pub-sub connection
        PubSubArrayRoot, // this is a pub-sub connection, and we've seen an array-root first element, waiting for the second
        OutOfBand, // we have determined that this is an out-of-band message
        RequestResponse, // we have determined that this is a request-response message
    }

    /// <summary>
    /// Initializes a <see cref="ScanState"/> instance.
    /// </summary>
    public static ref readonly ScanState Create(bool pubSubConnection) => ref pubSubConnection ? ref _pubSub : ref _default;

    private static readonly ScanState _pubSub = new ScanState(MessageKind.PubSubRoot), _default = default;

    private ScanState(MessageKind kind)
    {
        this = default;
        _kind = kind;
    }

    /// <summary>
    /// Gets whether the root element represents and out-of-band message.
    /// </summary>
    public bool IsOutOfBand => _kind is MessageKind.OutOfBand;

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
    /// Create a new value that can parse the supplied node (and subtree).
    /// </summary>
    internal ScanState(in RespReader reader)
    {
        Debug.Assert(reader.Prefix != RespPrefix.None);
        _totalBytes = 0;
        _delta = reader.Delta();
        _streamingAggregateDepth = reader.IsStreamingAggregate ? (ushort)1 : (ushort)0;
    }

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
#if DEBUG
            _elementCount++;
#endif
            if (!reader.IsAttribute)
            {
                switch (_kind)
                {
                    case MessageKind.Root:
                        _kind = reader.Prefix == RespPrefix.Push ? MessageKind.OutOfBand : MessageKind.RequestResponse;
                        break;
                    case MessageKind.PubSubRoot:
                        _kind = reader.Prefix switch
                        {
                            RespPrefix.Array => MessageKind.PubSubArrayRoot,
                            _ => MessageKind.OutOfBand, // in pub-sub, everything is OOB unless proven otherwise
                        };
                        break;
                    case MessageKind.PubSubArrayRoot:
                        // in pub-sub, the only request-response scenario is PING, which responds with an array with "ping" in the first element
                        _kind = reader.Prefix == RespPrefix.BulkString && reader.Is("pong"u8) ? MessageKind.RequestResponse : MessageKind.OutOfBand;
                        break;
                }
            }

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
        else if (reader.AggregateLength() > 0 && _streamingAggregateDepth != 0)
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
}
