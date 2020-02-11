using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;

namespace BedrockRespProtocol.Internal
{
    internal sealed class RespFormatter : IMessageReader<RespFrame>, IMessageWriter<RespFrame>
    {
        internal static RespFormatter Instance { get; } = new RespFormatter();
        private RespFormatter() { }

       
        bool IMessageReader<RespFrame>.TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RespFrame message)
        {
            if (RespFrame.TryParse(input, out message, out var end))
            {
                examined = consumed = end;
                return true;
            }
            message = default;
            return false;
        }

        void IMessageWriter<RespFrame>.WriteMessage(RespFrame message, IBufferWriter<byte> output)
            => message.Write(output);
    }
}
