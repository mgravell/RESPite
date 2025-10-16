using System.Buffers;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Writers;

namespace RESPite;

/// <summary>
/// Parses CLI-style commands (with escaping etc) into RESP.
/// </summary>
public class CommandParser
{
    private enum ParseState
    {
        None,
        Literal,
        SingleQuotedLiteral,
        DoubleQuotedLiteral,
        QuoteTerminator,
    }

    private sealed class ArrayPoolWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer = [];
        private int _committed;
        public void Advance(int count)
        {
            if (_committed + count > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            _committed += count;
        }

        private void Ensure(int sizeHint)
        {
            sizeHint = Math.Max(Math.Min(sizeHint, 16 * 1024), 128);
            if (_committed + sizeHint > _buffer.Length)
            {
                // grow
                var newArray = ArrayPool<byte>.Shared.Rent(_committed + sizeHint);
                _buffer.AsSpan(0, _committed).CopyTo(newArray);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newArray;
            }
        }
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(_committed);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(_committed);
        }

        public void Dispose()
        {
            var tmp = _buffer;
            _buffer = [];
            if (tmp is { Length: > 0 }) ArrayPool<byte>.Shared.Return(tmp);
        }
        public ReadOnlySpan<byte> Committed() => _buffer.AsSpan(0, _committed);

        internal byte[] Detach(out int committed)
        {
            var tmp = _buffer;
            committed = _committed;
            _committed = 0;
            _buffer = [];
            return tmp;
        }
    }

    /// <summary>
    /// Parse the given CLI-style command into RESP.
    /// </summary>
    public static Lease<byte> ParseResp(ReadOnlySpan<char> value)
    {
        RespWriter writer = default;
        var chunks = ParseTokens(value, ref writer, doWrite: false);
        using var buffer = new ArrayPoolWriter();
        writer = new(buffer);
        writer.WriteArray(chunks);
        ParseTokens(value, ref writer, doWrite: true);
        writer.Flush();

        var arr = buffer.Detach(out int committed);
        return new Lease<byte>(arr, committed);
    }
    private static int ParseTokens(ReadOnlySpan<char> value, ref RespWriter writer, bool doWrite)
    {
        int start = -1, chunks = 0;
        ParseState state = ParseState.None;
        static void AddChunk(ReadOnlySpan<char> chunk, ref RespWriter writer, ParseState state)
        {
            if (chunk.IndexOf('\\') < 0 | state is ParseState.Literal)
            {
                // no escaping needed
                writer.WriteBulkString(chunk);
                return;
            }
            // this is pretty CPU inefficient, but: rare, so: let's not worry about it for now; it would be better
            // to handle groups of characters instead of individuals
            byte[]? lease = null;
            int maxBytes = Encoding.UTF8.GetMaxByteCount(chunk.Length);
            Span<byte> span = maxBytes <= 128 ? stackalloc byte[128] : (lease = ArrayPool<byte>.Shared.Rent(maxBytes));
            Span<char> single = stackalloc char[1];
            int bytesUsed = 0;
            for (int i = 0; i < chunk.Length; i++)
            {
                var c = chunk[i];
                if (c == '\\')
                {
                    if (state is ParseState.DoubleQuotedLiteral // only double-quotes support embedded hex
                        && i + 3 < chunk.Length
                        && chunk[i + 1] == 'x'
                        && IsHex(chunk[i + 2], out var high) && IsHex(chunk[i + 3], out var lo))
                    {
                        span[bytesUsed++] = (byte)((high << 4) | lo);
                        i += 3; // consume the extra chars
                        continue;
                    }
                    if (i + 1 < chunk.Length)
                    {
                        var next = chunk[i + 1];
                        if (IsEscapeChar(ref next))
                        {
                            span[bytesUsed++] = (byte)next; // always ASCII
                            i++; // consume the extra char
                            continue;
                        }
                    }
                }

                if (c <= 127)
                {
                    span[bytesUsed++] = (byte)c;
                }
                else
                {
                    unsafe
                    {
                        fixed (byte* ptr = &span[bytesUsed])
                        {
                            bytesUsed += Encoding.UTF8.GetBytes(&c, 1, ptr, span.Length - bytesUsed);
                        }
                    }
                }
            }
            writer.WriteBulkString(span.Slice(0, bytesUsed));
            bool IsEscapeChar(ref char c)
            {
                switch (state)
                {
                    case ParseState.DoubleQuotedLiteral:
                        switch (c)
                        {
                            case '"':
                            case '\\':
                                return true;
                            case 'n':
                                c = '\n';
                                return true;
                            case 'r':
                                c = '\r';
                                return true;
                            case 't':
                                c = '\t';
                                return true;
                            case 'b':
                                c = '\b';
                                return true;
                            case 'a':
                                c = '\a';
                                return true;
                        }

                        break;
                    case ParseState.SingleQuotedLiteral:
                        switch (c)
                        {
                            case '\'':
                            case '\\':
                                return true;
                        }
                        break;
                }

                return false;
            }

            static bool IsHex(char c, out byte val)
            {
                if (c >= '0' && c <= '9')
                {
                    val = (byte)(c - '0');
                    return true;
                }
                if (c >= 'a' && c <= 'f')
                {
                    val = (byte)(c - 'a' + 10);
                    return true;
                }
                if (c >= 'A' && c <= 'F')
                {
                    val = (byte)(c - 'A' + 10);
                    return true;
                }
                val = 0;
                return false;
            }
        }

        void AfterChunk()
        {
            start = -1;
            chunks++;
            state = state switch
            {
                ParseState.SingleQuotedLiteral or ParseState.DoubleQuotedLiteral => ParseState.QuoteTerminator,
                ParseState.Literal => ParseState.None,
                _ => throw new InvalidOperationException($"Unexpected state {state} after chunk"),
            };
        }

        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (state)
            {
                case ParseState.QuoteTerminator when char.IsWhiteSpace(c): // whitespace after a quoted token
                    state = ParseState.None;
                    break;
                case ParseState.QuoteTerminator: // something *other than* whitespace after a quoted token
                    throw new FormatException($"Unexpected character '{c}' after quoted token");
                case ParseState.None when char.IsWhiteSpace(c): // whitespace before a token
                    break;
                case ParseState.None when c == '\'': // start a single-quote block
                    start = i + 1;
                    state = ParseState.SingleQuotedLiteral;
                    break;
                case ParseState.None when c == '"': // start a double-quote block
                    start = i + 1;
                    state = ParseState.DoubleQuotedLiteral;
                    break;
                case ParseState.None: // start a basic literal
                    start = i;
                    state = ParseState.Literal;
                    break;
                case ParseState.SingleQuotedLiteral when c == '\'' & value[i - 1] != '\\': // terminate a single-quote block
                    if (doWrite) AddChunk(value.Slice(start, i - start), ref writer, state);
                    AfterChunk();
                    break;
                case ParseState.DoubleQuotedLiteral when c == '"' & value[i - 1] != '\\': // terminate a double-quote block
                    if (doWrite) AddChunk(value.Slice(start, i - start), ref writer, state);
                    AfterChunk();
                    break;
                case ParseState.Literal when char.IsWhiteSpace(c):
                    if (doWrite) AddChunk(value.Slice(start, i - start), ref writer, state);
                    AfterChunk();
                    break;
                case ParseState.Literal:
                case ParseState.SingleQuotedLiteral:
                case ParseState.DoubleQuotedLiteral:
                    break; // keep capturing characters
                default:
                    throw new InvalidOperationException($"Unexpected state {state}, character {c}");
            }
        }
        // handle trailing data
        if (start >= 0)
        {
            switch (state)
            {
                case ParseState.SingleQuotedLiteral: // note we allow unterminated quotes
                case ParseState.DoubleQuotedLiteral:
                case ParseState.Literal:
                    if (doWrite) AddChunk(value.Slice(start), ref writer, state);
                    AfterChunk();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected state {state} at end of string");
            }
        }
        return chunks;
    }
}
