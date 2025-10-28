using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RESPite;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;

namespace StackExchange.Redis;

internal static class RespClient
{
    public static string ParseCommand(string line, out object[] args)
    {
        try
        {
            return Utils.Parse(line, out args);
        }
        catch (Exception ex)
        {
            // log only (treat as blank input (i.e. repeat input loop)
            WriteLine(ex.Message, ConsoleColor.Red, ConsoleColor.White);
            args = Array.Empty<object>();
            return "";
        }
    }

    private static void Write(string message, ConsoleColor? foreground, ConsoleColor? background, bool newLine = false)
    {
        var fg = Console.ForegroundColor;
        var bg = Console.BackgroundColor;
        try
        {
            if (foreground != null) Console.ForegroundColor = foreground.Value;
            if (background != null) Console.BackgroundColor = background.Value;
            Console.Write(message);
        }
        finally
        {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg;
        }
        if (newLine) Console.WriteLine();
    }

    private static void WriteLine(string message, ConsoleColor? foreground, ConsoleColor? background) => Write(message, foreground, background, true);

    internal static async Task RunClient(IRequestResponseTransport transport, ImmutableArray<string> handshake, ImmutableArray<string> command, int repeat, double interval, int? db)
    {
        try
        {
            while (true)
            {
                Lease<byte> cmd;
                string? hint = null;
                if (!handshake.IsDefaultOrEmpty)
                {
                    cmd = CommandParser.ParseResp(handshake.AsSpan());
                    if (string.Equals("HELLO", handshake[0], StringComparison.OrdinalIgnoreCase)
                        && handshake.Length == 2)
                    {
                        hint = $"{handshake[0]} {handshake[1]}"; // safe and meaningful to include
                    }
                    else
                    {
                        hint = $"{handshake[0]}...";
                    }
                    handshake = [];
                }
                else if (db.HasValue)
                {
                    hint = $"SELECT {db}";
                    cmd = CommandParser.ParseResp(hint);
                    hint = $"SELECT {db}";
                    db = null;
                }
                else if (!command.IsDefaultOrEmpty)
                {
                    if (repeat <= 0)
                    {
                        cmd = default;
                    }
                    else
                    {
                        cmd = CommandParser.ParseResp(string.Join(' ', command));
                        hint = command.Length == 1 ? command[0] : $"{command[0]}...";
                        repeat--;
                        if (repeat != 0 && interval > 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(interval));
                        }
                    }
                }
                else
                {
                    var line = ReadLine();
                    cmd = line is null ? default : CommandParser.ParseResp(line);
                }

                if (cmd.IsNull)
                {
                    break; // EOF
                }
                if (!cmd.IsEmpty)
                {
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        WriteLine(hint, null, null);
                    }
                    WriteResult(await transport.SendAsync(cmd.Memory, CommandWriter.Raw, LeasedRespResult.Reader));
                }
                cmd.Dispose();
            }
        }
        catch (Exception ex)
        {
            WriteLine(ex.Message, ConsoleColor.Red, ConsoleColor.White);
            // and exit, no idea what happened
        }

        static void WriteResult(LeasedRespResult result)
        {
            var reader = new RespReader(result.Span);
            if (reader.TryReadNext())
            {
                RespReader dummyKey = default;
                WriteValue(ref dummyKey, ref reader, 0, -1);
            }
        }

        static void WriteValue(ref RespReader headerKey, ref RespReader reader, int indent, int index)
        {
            if (reader.IsScalar)
            {
                WriteString(
                    ref headerKey,
                    ref reader,
                    indent,
                    index,
                    reader.IsError ? ConsoleColor.Red : null,
                    reader.IsError ? ConsoleColor.Gray : null);
            }
            else if (reader.IsAggregate)
            {
                WriteArray(ref headerKey, ref reader, indent, index);
            }
        }

        static void WriteArray(ref RespReader headerKey, ref RespReader reader, int indent, int index)
        {
            var prefix = reader.Prefix;
            WriteHeader(ref headerKey, prefix, indent, index);
            if (reader.IsNull)
            {
                WriteNull();
            }
            else
            {
                var count = reader.AggregateLength();
                if (count == 0)
                {
                    WriteLine("(empty)", ConsoleColor.Green, ConsoleColor.DarkGray);
                }
                else
                {
                    WriteLine($"{count}", ConsoleColor.Green, ConsoleColor.DarkGray);
                }

                // using iterator approach so that streaming is handled automatically
                int i = 1;
                var iter = reader.AggregateChildren();
                if ((prefix is RespPrefix.Map or RespPrefix.Attribute) && WriteAsScalarMap(iter))
                {
                    while (iter.MoveNext())
                    {
                        var key = iter.Value;
                        key.MoveNext();

                        iter.MoveNext(); // we already checked this in WriteAsScalarMap
                        var value = iter.Value;
                        value.MoveNext();

                        WriteValue(ref key,  ref value, indent + 1, i);
                        key.DemandEnd();
                        value.DemandEnd();
                        i++;
                    }
                }
                else
                {
                    RespReader dummyKey = default;
                    while (iter.MoveNext())
                    {
                        ref RespReader child = ref iter.Value;
                        child.MoveNext();
                        WriteValue(ref dummyKey, ref child, indent + 1, i);
                        child.DemandEnd();
                        i++;
                    }
                }
                iter.MovePast(out reader);

                static bool WriteAsScalarMap(RespReader.AggregateEnumerator iter)
                {
                    // check for an even number of scalar, non-streaming elements
                    while (iter.MoveNext())
                    {
                        ref RespReader child = ref iter.Value;
                        if (!child.TryMoveNext() || child.IsAggregate || child.IsStreaming || child.IsError)
                        {
                            return false; // key is not a simple scalar
                        }

                        if (!iter.MoveNext())
                        {
                            return false; // odd number of elements
                        }
                    }
                    return true;
                }
            }
        }

        static void Indent(int indent)
        {
            while (indent-- > 0) Write("  ", null, null);
        }

        static void WriteHeader(ref RespReader headerKey, RespPrefix prefix, int indent, int index)
        {
            Indent(indent);
            if (index >= 0)
            {
                Write($"[{index}]", ConsoleColor.White, ConsoleColor.DarkBlue);
            }

            if (headerKey.Prefix is not RespPrefix.None)
            {
                Write(" ", null, null);
                Write(((char)headerKey.Prefix).ToString(), ConsoleColor.White, ConsoleColor.DarkBlue);
                Write(" ", null, null);
                if (headerKey.IsNull)
                {
                    WriteNull(newLine: false);
                }
                else
                {
                    Write(CommandParser.ReadEscaped(ref headerKey, false), null, null, newLine: false);
                }
                Write(" => ", null, null, newLine: false);
            }
            Write(((char)prefix).ToString(), ConsoleColor.White, ConsoleColor.DarkBlue);
            Write(" ", null, null);
        }
        static void WriteString(ref RespReader headerKey, ref RespReader reader, int indent, int index, ConsoleColor? foreground = null, ConsoleColor? background = null)
        {
            WriteHeader(ref headerKey, reader.Prefix, indent, index);
            if (reader.IsNull)
            {
                WriteNull();
            }
            else
            {
                WriteLine(CommandParser.ReadEscaped(ref reader, false), foreground, background);
            }
        }

        static void WriteNull(bool newLine = true)
        {
            Write("(nil)", ConsoleColor.Blue, ConsoleColor.Yellow, newLine);
        }

        static string? ReadLine()
        {
            Console.Write("> ");
            return Console.ReadLine();
        }
    }
}
