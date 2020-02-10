using Bedrock.Framework.Protocols;
using Microsoft.AspNetCore.Connections;
using Resp;
using System.Threading;
using System.Threading.Tasks;

namespace BedrockRespProtocol
{

    public sealed class RespBedrockProtocol : RespConnection
    {
        // private readonly ConnectionContext _connection;
        private readonly ProtocolReader _reader;
        private readonly ProtocolWriter _writer;

        public RespBedrockProtocol(ConnectionContext connection)
        {
            // _connection = connection;
            _reader = connection.CreateReader();
            _writer = connection.CreateWriter();
        }

        public override void Send(in RawFrame frame)
        {
            var vt = SendAsync(frame, default);
            if (!vt.IsCompletedSuccessfully) vt.AsTask().Wait();
        }

        public override RawFrame Receive()
        {
            var vt = ReceiveAsync(default);
            return vt.IsCompletedSuccessfully ? vt.Result : vt.AsTask().Result;
        }


        public override ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken)
            => _writer.WriteAsync<RawFrame>(Resp2ClientWriter.Instance, frame, cancellationToken);

        public override ValueTask<RawFrame> ReceiveAsync(CancellationToken cancellationToken)
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
