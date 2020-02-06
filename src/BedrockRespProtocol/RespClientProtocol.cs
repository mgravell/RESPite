using Bedrock.Framework.Protocols;
using Microsoft.AspNetCore.Connections;
using Resp;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BedrockRespProtocol
{
    public sealed class RespClientProtocol
    {
        // private readonly ConnectionContext _connection;
        private readonly ProtocolReader _reader;
        private readonly ProtocolWriter _writer;

        public RespClientProtocol(ConnectionContext connection)
        {
            // _connection = connection;
            _reader = connection.CreateReader();
            _writer = connection.CreateWriter();
        }
        public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await SendAsync(RedisFrame.Ping, cancellationToken).ConfigureAwait(false);
            using var pong = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!(pong is RedisSimpleString rss && rss.Equals("PONG", StringComparison.OrdinalIgnoreCase))) Wat();
            return after - before;
        }

        public async ValueTask<TimeSpan> PingRawAsync(CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await SendAsync(RawFrame.Ping, cancellationToken).ConfigureAwait(false);
            using var pong = await ReadRawAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }
        static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static ReadOnlySpan<byte> Pong => new byte[] { (byte)'p', (byte)'o', (byte)'n', (byte)'g' };

        public ValueTask SendAsync(RedisFrame frame, CancellationToken cancellationToken = default)
            => _writer.WriteAsync<RedisFrame>(Resp2ClientWriter.Instance, frame, cancellationToken);

        public ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken = default)
            => _writer.WriteAsync<RawFrame>(Resp2ClientWriter.Instance, frame, cancellationToken);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowCanceled() => throw new OperationCanceledException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowAborted() => throw new ConnectionAbortedException();

        public ValueTask<RedisFrame> ReadAsync(CancellationToken cancellationToken = default)
            => ReadAsync<RedisFrame>(_reader, Resp2ClientReader.Instance, cancellationToken);

        public ValueTask<RawFrame> ReadRawAsync(CancellationToken cancellationToken = default)
            => ReadAsync<RawFrame>(_reader, Resp2ClientReader.Instance, cancellationToken);

        private static ValueTask<T> ReadAsync<T>(ProtocolReader source, IMessageReader<T> parser, CancellationToken cancellationToken)
        {
            var result = source.ReadAsync<T>(parser, cancellationToken);
            // avoid the async machinery if we already have the result on the pipe
            return result.IsCompletedSuccessfully ? new ValueTask<T>(Validate(source, result.Result)) : Awaited(source, result);

            static async ValueTask<T> Awaited(ProtocolReader reader, ValueTask<ProtocolReadResult<T>> result)
                => Validate(reader, await result.ConfigureAwait(false));

            static T Validate(ProtocolReader reader, in ProtocolReadResult<T> result)
            {
                reader.Advance();
                if (result.IsCanceled) ThrowCanceled();
                if (result.IsCompleted) ThrowAborted();
                return result.Message;
            }
        }
    }
}
