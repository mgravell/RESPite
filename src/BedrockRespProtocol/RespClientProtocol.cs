using Bedrock.Framework.Protocols;
using BedrockRespProtocol.Internal;
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

        public override void Send(in RespFrame frame)
        {
            var vt = SendAsync(frame, default);
            if (!vt.IsCompletedSuccessfully) vt.AsTask().Wait();
        }

        public override RespFrame Receive()
        {
            var vt = ReceiveAsync(default);
            return vt.IsCompletedSuccessfully ? vt.Result : vt.AsTask().Result;
        }

        public override ValueTask SendAsync(RespFrame frame, CancellationToken cancellationToken)
            => _writer.WriteAsync<RespFrame>(RespFormatter.Instance, frame, cancellationToken);

        public override ValueTask<RespFrame> ReceiveAsync(CancellationToken cancellationToken)
        {
            var result = _reader.ReadAsync<RespFrame>(RespFormatter.Instance, cancellationToken);
            // avoid the async machinery if we already have the result on the pipe
            return result.IsCompletedSuccessfully ? new ValueTask<RespFrame>(Validate(_reader, result.Result)) : Awaited(_reader, result);

            static async ValueTask<RespFrame> Awaited(ProtocolReader reader, ValueTask<ProtocolReadResult<RespFrame>> result)
                => Validate(reader, await result.ConfigureAwait(false));

            static RespFrame Validate(ProtocolReader reader, in ProtocolReadResult<RespFrame> result)
            {
                reader.Advance();
                if (result.IsCanceled) ThrowCanceled();
                if (result.IsCompleted) ThrowAborted();
                return result.Message;
            }
        }
    }
}
