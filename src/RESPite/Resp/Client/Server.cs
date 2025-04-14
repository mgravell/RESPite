using System.Buffers;
using System.Runtime.CompilerServices;
using RESPite.Messages;
using RESPite.Resp.Commands;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.Client;

/// <summary>
/// Server commands.
/// </summary>
public static class Server
{
    /// <summary>
    /// Sends a message to the server and validates that the server responds successfully.
    /// </summary>
    public static readonly RespCommand<Empty, Empty> PING = new(Default, reader: PongEchoReader.Instance);

    /// <summary>
    /// Sends a message to the server and validates that the same message is received as a response.
    /// </summary>
    public static readonly RespCommand<SimpleString, Empty> ECHO = new(Default, reader: PongEchoReader.Instance);

    /// <summary>
    /// Changes database.
    /// </summary>
    public static readonly RespCommand<int, Empty> SELECT = new(Default);

    /// <summary>
    /// Reads PONG responses.
    /// </summary>
    private sealed class PongEchoReader : IRespReader<Empty, Empty>, IRespReader<SimpleString, Empty>
    {
        internal static readonly PongEchoReader Instance = new();
        private PongEchoReader()
        {
        }

        Empty IReader<Empty, Empty>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            return Read(in request, ref reader);
        }

        Empty IReader<SimpleString, Empty>.Read(in SimpleString request, in ReadOnlySequence<byte> content)
        {
            var reader = new RespReader(content);
            return Read(in request, ref reader);
        }

        public Empty Read(in Empty request, ref RespReader reader)
        {
            reader.MoveNext(RespPrefix.SimpleString);
            if (!reader.Is("PONG"u8)) ThrowMissingExpected("PONG");
            return default;
        }

        public Empty Read(in SimpleString request, ref RespReader reader)
        {
            reader.MoveNext(RespPrefix.BulkString);

            if (!reader.Is(request)!) ThrowMissingExpected(request);
            return Empty.Value;
        }

        internal static void ThrowMissingExpected(in SimpleString expected, [CallerMemberName] string caller = "")
            => throw new InvalidOperationException($"Did not receive expected response: '{expected}'");
    }

    /// <summary>
    /// This is a container command for client connection commands.
    /// </summary>
    public static partial class CLIENT
    {
        /// <summary>
        /// The CLIENT SETNAME command assigns a name to the current connection.
        /// </summary>
        public static readonly RespCommand<string, Empty> SETNAME = new(Default, command: nameof(CLIENT), writer: ClientNameWriter.Instance);

        /// <summary>
        /// The CLIENT SETINFO command assigns various info attributes to the current connection which are displayed in the output of CLIENT LIST and CLIENT INFO.
        /// </summary>
        public static readonly RespCommand<(string Attribute, string Value), Empty> SETINFO = new(Default, command: nameof(CLIENT), writer: ClientInfoWriter.Instance);

        private sealed class ClientNameWriter(string command = nameof(CLIENT)) : CommandWriter<string>(command, 2)
        {
            public static ClientNameWriter Instance = new();

            protected override IRespWriter<string> Create(string command) => new ClientNameWriter(command);

            protected override void WriteArgs(in string request, ref RespWriter writer)
            {
                writer.WriteRaw("$7\r\nSETNAME\r\n"u8);
                writer.WriteBulkString(request);
            }
        }

        private sealed class ClientInfoWriter(string command = nameof(CLIENT)) : CommandWriter<(string Attribute, string Value)>(command, 3)
        {
            public static ClientInfoWriter Instance = new();

            protected override IRespWriter<(string Attribute, string Value)> Create(string command) => new ClientInfoWriter(command);

            protected override void WriteArgs(in (string Attribute, string Value) request, ref RespWriter writer)
            {
                writer.WriteRaw("$7\r\nSETINFO\r\n"u8);
                writer.WriteBulkString(request.Attribute);
                writer.WriteBulkString(request.Value);
            }
        }
    }

    /// <summary>
    /// This is a container command for runtime configuration commands.
    /// </summary>
    public static class CONFIG
    {
        /// <summary>
        /// The CONFIG GET command is used to read the configuration parameters of a running Redis server. Not all the configuration parameters are supported in Redis 2.4, while Redis 2.6 can read the whole configuration of a server using this command.
        /// </summary>
        public static readonly RespCommand<string, LeasedString> GET = new(Default, command: nameof(CONFIG), writer: ConfigGetWriter.Instance);

        private sealed class ConfigGetWriter(string command = nameof(CONFIG)) : CommandWriter<string>(command, 2)
        {
            public static ConfigGetWriter Instance = new();

            protected override IRespWriter<string> Create(string command) => new ConfigGetWriter(command);

            protected override void WriteArgs(in string request, ref RespWriter writer)
            {
                writer.WriteRaw("$3\r\nGET\r\n"u8);
                writer.WriteBulkString(request);
            }
        }
    }

    /// <summary>
    /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
    /// </summary>
    public static readonly RespCommand<string, LeasedString> INFO = new(Default);
}
