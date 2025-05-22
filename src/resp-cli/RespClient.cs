using System.Collections.Immutable;
using System.Globalization;
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

    private static void Write(string message, ConsoleColor? foreground, ConsoleColor? background)
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
    }

    private static void WriteLine(string message, ConsoleColor? foreground, ConsoleColor? background)
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
        Console.WriteLine();
    }

    internal static async Task RunClient(IRequestResponseTransport transport, ImmutableArray<string> handshake, ImmutableArray<string> command, int repeat, double interval, int? db)
    {
        try
        {
            while (true)
            {
                LeasedStrings cmd;
                string? hint = null;
                if (!handshake.IsDefaultOrEmpty)
                {
                    cmd = new(handshake);
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
                    cmd = new(["SELECT", db.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)]);
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
                        cmd = new(command);
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
                    cmd = line is null ? default : new(Utils.Tokenize(line).ToImmutableArray());
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
                    WriteResult(await transport.SendAsync(cmd, CommandWriter.AdHoc, LeasedRespResult.Reader));
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
                WriteValue(ref reader, 0, -1);
            }
        }

        static void WriteValue(ref RespReader reader, int indent, int index)
        {
            if (reader.IsScalar)
            {
                WriteString(
                    ref reader,
                    indent,
                    index,
                    reader.IsError ? ConsoleColor.Red : null,
                    reader.IsError ? ConsoleColor.Gray : null);
            }
            else if (reader.IsAggregate)
            {
                WriteArray(ref reader, indent, index);
            }
        }

        static void WriteArray(ref RespReader reader, int indent, int index)
        {
            WriteHeader(reader.Prefix, indent, index);
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
                var iter = reader.AggregateChildren();
                int i = 0;
                while (iter.MoveNext())
                {
                    ref RespReader child = ref iter.Value;
                    child.MoveNext();
                    WriteValue(ref child, indent, i);
                    child.DemandEnd();
                    iter.MovePast(out reader);
                }
            }
        }

        static void Indent(int indent)
        {
            while (indent-- > 0) Write(" ", null, null);
        }

        static void WriteHeader(RespPrefix prefix, int indent, int index)
        {
            Indent(indent);
            if (index >= 0)
            {
                Write($"[{index}]", ConsoleColor.White, ConsoleColor.DarkBlue);
            }
            Write(((char)prefix).ToString(), ConsoleColor.White, ConsoleColor.DarkBlue);
            Write(" ", null, null);
        }
        static void WriteString(ref RespReader reader, int indent, int index, ConsoleColor? foreground = null, ConsoleColor? background = null)
        {
            WriteHeader(reader.Prefix, indent, index);
            if (reader.IsNull)
            {
                WriteNull();
            }
            else
            {
                WriteLine(reader.ReadString() ?? "", foreground, background);
            }
        }

        static void WriteNull()
        {
            WriteLine("(nil)", ConsoleColor.Blue, ConsoleColor.Yellow);
        }

        static string? ReadLine()
        {
            Console.Write("> ");
            return Console.ReadLine();
        }
    }
}
