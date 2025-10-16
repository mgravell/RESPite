using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;
using Terminal.Gui;

namespace StackExchange.Redis;

public static class Utils
{
    public static void Append(this TextView log, string msg)
    {
        Application.Invoke(() =>
        {
            try
            {
                log.MoveEnd();
                log.ReadOnly = false;
                log.InsertText(msg + Environment.NewLine);
                log.ReadOnly = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "TFMs")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1845:Use span-based 'string.Concat'", Justification = "TFMs")]
    internal static string Truncate(string? value, int length)
    {
        value ??= "";
        if (value.Length > length)
        {
            return length <= 1 ? "\u2026" : value.Substring(0, length - 1) + "\u2026";
        }
        return value;
    }

    internal static async Task<IRequestResponseTransport?> ConnectAsync(
        ConnectionOptionsBag options,
        IFrameScanner<ScanState>? frameScanner = null,
        FrameValidation validateOutbound = FrameValidation.Debug,
        Stream? client = null,
        CancellationToken endOfLife = default)
    {
        Socket? socket = null;
        Stream? server = null;
        try
        {
            var ep = BuildEndPoint(options.Host, options.Port);
            socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            options.Log?.Invoke($"Connecting to {options.Host} on TCP port {options.Port}...");
            await socket.ConnectAsync(ep, endOfLife);

            server = new NetworkStream(socket);

            if (options.Tls)
            {
                options.Log?.Invoke("Establishing TLS...");
                var ssl = new SslStream(server);
                server = ssl;
                var sslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = options.GetRemoteCertificateValidationCallback(),
                    LocalCertificateSelectionCallback = options.GetLocalCertificateSelectionCallback(),
                    TargetHost = string.IsNullOrWhiteSpace(options.Sni) ? options.Host : options.Sni,
                };
                await ssl.AuthenticateAsClientAsync(sslOptions, endOfLife);
            }

            if (client is null)
            {
                return server.CreateTransport(autoFlush: options.AutoFlush, debugLog: options.DebugLog).RequestResponse(frameScanner ?? RespFrameScanner.Default, validateOutbound, options.DebugLog);
            }
            else
            {
                StartProxying(client, server, frameScanner ?? RespFrameScanner.Default, endOfLife);
                return null;
            }
        }
        catch (Exception ex)
        {
            server?.Dispose();
            client?.Dispose();
            socket?.Dispose();

            options.Log?.Invoke(ex.Message);
            return null;
        }
    }

    private static void StartProxying(Stream client, Stream server, IFrameScanner<ScanState> scanner, CancellationToken endOfLife)
    {
        // client-to-server
        var outbound = new System.IO.Pipelines.Pipe();
        _ = Task.Run(() => client.CopyToAsync(outbound.Writer, endOfLife));
        _ = Task.Run(() => ReadAllAsync(outbound.Reader, server, scanner, true, endOfLife));

        var inbound = new System.IO.Pipelines.Pipe();
        _ = Task.Run(() => server.CopyToAsync(inbound.Writer, endOfLife));
        _ = Task.Run(() => ReadAllAsync(inbound.Reader, client, scanner, false, endOfLife));
    }

    private static async Task ReadAllAsync(PipeReader source, Stream destination, IFrameScanner<ScanState> scanner, bool outbound, CancellationToken endOfLife)
    {
        ScanState scanState = default;
        try
        {
            if (scanner is IFrameScannerLifetime<ScanState> lifetime)
            {
                lifetime.OnInitialize(out scanState);
            }
            while (true) // successive frames
            {
                FrameScanInfo scanInfo = new(outbound);
                scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                while (true) // incremental read of single frame
                {
                    // we can pass partial fragments to an incremental scanner, but we need the entire fragment
                    // for deframe; as such, "skip" is our progress into the current frame for an incremental scanner
                    var read = await source.ReadAsync(endOfLife);
                    var entireBuffer = read.Buffer;
                    var workingBuffer = scanInfo.BytesRead == 0 ? entireBuffer : entireBuffer.Slice(scanInfo.BytesRead);
                    var status = workingBuffer.IsEmpty ? OperationStatus.NeedMoreData : scanner.TryRead(ref scanState, in workingBuffer, ref scanInfo);

                    switch (status)
                    {
                        case OperationStatus.InvalidData:
                            // we always call advance as a courtesy for backends that need per-read advance
                            source.AdvanceTo(entireBuffer.End, entireBuffer.End);
                            throw new ProtocolViolationException("Invalid RESP data encountered");
                        case OperationStatus.NeedMoreData:
                            source.AdvanceTo(entireBuffer.Start, entireBuffer.End);
                            break;
                        case OperationStatus.Done when scanInfo.BytesRead <= 0:
                            // if we're not making progress, we'd loop forever
                            source.AdvanceTo(entireBuffer.End, entireBuffer.End);
                            throw new InvalidOperationException("Not making progress!");
                        case OperationStatus.Done:
                            long bytesRead = scanInfo.BytesRead; // snapshot for our final advance
                            workingBuffer = entireBuffer.Slice(0, bytesRead); // includes head and trail data
                            scanner.Trim(ref scanState, ref workingBuffer, ref scanInfo);

                            if (workingBuffer.IsSingleSegment)
                            {
                                await destination.WriteAsync(workingBuffer.First, endOfLife);
                            }
                            else
                            {
                                foreach (var chunk in workingBuffer)
                                {
                                    await destination.WriteAsync(chunk, endOfLife);
                                }
                            }

                            source.AdvanceTo(workingBuffer.End, workingBuffer.End);

                            // prepare for next frame
                            scanInfo = new(outbound);
                            scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                            continue;
                        default:
                            source.AdvanceTo(entireBuffer.End, entireBuffer.End);
                            throw new InvalidOperationException($"Invalid status: {status}");
                    }
                }
            }
        }
        finally
        {
            // OnComplete();
            if (scanner is IFrameScannerLifetime<ScanState> lifetime)
            {
                lifetime.OnComplete(ref scanState);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Clarity")]
    internal static ImmutableArray<string> GetHandshake(ConnectionOptionsBag options)
    {
        if (options.Resp3)
        {
            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                if (string.IsNullOrWhiteSpace(options.User))
                {
                    return ["HELLO", "3", "AUTH", "default", options.Password];
                }
                else
                {
                    return ["HELLO", "3", "AUTH", options.User, options.Password];
                }
            }
            else
            {
                return ["HELLO", "3"];
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Password))
        {
            if (string.IsNullOrWhiteSpace(options.User))
            {
                return ["AUTH", options.Password];
            }
            else
            {
                return ["AUTH", options.User, options.Password];
            }
        }
        return [];
    }

    internal static string GetSimpleText(in ReadOnlySequence<byte> content, AggregateMode childMode = AggregateMode.Full, int sizeHint = int.MaxValue)
    {
        try
        {
            var reader = new RespReader(content);
            var sb = new StringBuilder();
            if (TryGetSimpleText(sb, ref reader, childMode, sizeHint: sizeHint) && !reader.TryReadNext())
            {
                return sb.ToString();
            }
        }
        catch
        {
            Debug.WriteLine(Encoding.UTF8.GetString(content));
            throw;
        }
        return Encoding.UTF8.GetString(content); // fallback
    }

    internal enum AggregateMode
    {
        Full,
        CountAndConsume,
        CountOnly,
    }

    public static string GetCommandText(in ReadOnlySequence<byte> payload, int sizeHint = int.MaxValue)
    {
        RespReader reader = new(payload);
        return GetCommandText(ref reader, sizeHint);
    }

    public static string GetCommandText(ref RespReader reader, int sizeHint = int.MaxValue)
    {
        reader.MoveNext(RespPrefix.Array);

        var len = reader.AggregateLength();
        reader.MoveNext(RespPrefix.BulkString);
        reader.DemandNotNull();
        string cmd = reader.ReadString()!;
        if (len != 1)
        {
            var sb = new StringBuilder(cmd);
            for (int i = 1; i < len; i++)
            {
                reader.MoveNext(RespPrefix.BulkString);
                reader.DemandNotNull();
                string orig = reader.ReadString()!;
                var s = Escape(reader.ReadString());
                if (orig.Contains(' ') || orig.Contains('\"')) s = "\"" + s + "\"";
                sb.Append(' ').Append(s);
            }
            cmd = sb.ToString();
        }
        reader.DemandEnd();
        return cmd;
    }

    private static string? Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        value = value.Replace("'", "\\'");
        value = value.Replace("\"", "\\\"");
        return value;
    }

    internal static bool TryGetSimpleText(StringBuilder sb, ref RespReader reader, AggregateMode aggregateMode = AggregateMode.Full, int sizeHint = int.MaxValue)
    {
        if (!reader.TryReadNext())
        {
            return false;
        }

        char prefix = (char)reader.Prefix;

        if (reader.IsNull)
        {
            sb.Append(prefix).Append("(null)");
            return true;
        }
        if (reader.IsScalar)
        {
            sb.Append(prefix);
            switch (reader.Prefix)
            {
                case RespPrefix.SimpleString:
                    sb.Append(Escape(reader.ReadString()));
                    break;
                case RespPrefix.BulkString:
                    sb.Append('"').Append(Escape(reader.ReadString())).Append('"');
                    break;
                case RespPrefix.VerbatimString:
                    sb.Append("\"\"\"").Append(Escape(reader.ReadString())).Append("\"\"\"");
                    break;
                default:
                    sb.Append(reader.ReadString());
                    break;
            }
            return true;
        }
        if (reader.IsAggregate)
        {
            var count = reader.AggregateLength();

            sb.Append(prefix).Append(count);
            switch (aggregateMode)
            {
                case AggregateMode.Full when count == 0:
                case AggregateMode.CountOnly:
                    return true;
                case AggregateMode.CountAndConsume:
                    reader.SkipChildren();
                    return true;
            }

            sb.Append(" [");
            for (int i = 0; i < count; i++)
            {
                if (i != 0 && sb.Length < sizeHint) sb.Append(',');
                if (sb.Length < sizeHint)
                {
                    if (!TryGetSimpleText(sb, ref reader, aggregateMode, sizeHint))
                    {
                        return false;
                    }
                }
                else
                {
                    // skip!
                    if (!reader.TryReadNext()) return false;
                    if (reader.IsAggregate) reader.SkipChildren();
                }
            }
            if (sb.Length < sizeHint) sb.Append(']');
            return true;
        }
        return false;
    }

    public static EndPoint BuildEndPoint(string host, int port) =>
        IPAddress.TryParse(host, out var ipAddress) ? new IPEndPoint(ipAddress, port) : new DnsEndPoint(host, port);

    public static string Parse(string value, out object[] args)
    {
        args = [];
        using var iter = Tokenize(value).GetEnumerator();
        if (iter.MoveNext())
        {
            var cmd = iter.Current;
            List<object>? list = null;
            while (iter.MoveNext())
            {
                (list ??= []).Add(iter.Current);
            }
            if (list is not null) args = [.. list];
            return cmd;
        }
        return "";
    }

    public static IEnumerable<string> Tokenize(string value)
    {
        bool inQuote = false, prevWhitespace = true;
        int startIndex = -1;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"' when inQuote: // end the current quoted string
                    yield return value.Substring(startIndex, i - startIndex);
                    startIndex = -1;
                    inQuote = false;
                    break;
                case '"' when startIndex < 0: // start a new quoted string
                    if (!prevWhitespace) UnableToParse();
                    inQuote = true;
                    startIndex = i + 1;
                    break;
                case '"':
                    UnableToParse();
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        if (startIndex >= 0 && !inQuote) // end non-quoted string
                        {
                            yield return value.Substring(startIndex, i - startIndex);
                            startIndex = -1;
                        }
                    }
                    else if (startIndex < 0) // start a new non-quoted token
                    {
                        if (!prevWhitespace) UnableToParse();

                        startIndex = i;
                    }
                    break;
            }
            prevWhitespace = !inQuote && char.IsWhiteSpace(c);
        }
        // anything left
        if (startIndex >= 0)
        {
            yield return value.Substring(startIndex: startIndex);
        }

        static void UnableToParse() => throw new FormatException("Unable to parse input");
    }
}
