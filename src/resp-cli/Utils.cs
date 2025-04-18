﻿using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Transports;

namespace StackExchange.Redis;

public static class Utils
{
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
        FrameValidation validateOutbound = FrameValidation.Debug)
    {
        Socket? socket = null;
        Stream? conn = null;
        try
        {
            var ep = BuildEndPoint(options.Host, options.Port);
            socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            options.Log?.Invoke($"Connecting to {options.Host} on TCP port {options.Port}...");
            await socket.ConnectAsync(ep);

            conn = new NetworkStream(socket);
            if (options.Tls)
            {
                options.Log?.Invoke("Establishing TLS...");
                var ssl = new SslStream(conn);
                conn = ssl;
                var sslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = options.GetRemoteCertificateValidationCallback(),
                    LocalCertificateSelectionCallback = options.GetLocalCertificateSelectionCallback(),
                    TargetHost = string.IsNullOrWhiteSpace(options.Sni) ? options.Host : options.Sni,
                };
                await ssl.AuthenticateAsClientAsync(sslOptions);
            }

            return conn.CreateTransport(autoFlush: options.AutoFlush, debugLog: options.DebugLog).RequestResponse(frameScanner ?? RespFrameScanner.Default, validateOutbound, options.DebugLog);
        }
        catch (Exception ex)
        {
            conn?.Dispose();
            socket?.Dispose();

            options.Log?.Invoke(ex.Message);
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Clarity")]
    internal static string GetHandshake(ConnectionOptionsBag options)
    {
        if (options.Resp3)
        {
            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                if (string.IsNullOrWhiteSpace(options.User))
                {
                    return $"HELLO 3 AUTH default {options.Password}";
                }
                else
                {
                    return $"HELLO 3 AUTH {options.User} {options.Password}";
                }
            }
            else
            {
                return "HELLO 3";
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Password))
        {
            if (string.IsNullOrWhiteSpace(options.User))
            {
                return $"AUTH {options.User} {options.Password}";
            }
            else
            {
                return $"AUTH {options.Password}";
            }
        }
        return "";
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
