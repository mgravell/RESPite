using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;

namespace BedrockRespProtocol.Internal
{
    internal sealed class RespFormatter : IMessageReader<RawFrame>, IMessageWriter<RawFrame>
    {
        internal static RespFormatter Instance { get; } = new RespFormatter();
        private RespFormatter() { }

       
        bool IMessageReader<RawFrame>.TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RawFrame message)
        {
            if (RawFrame.TryParse(input, out message, out var end))
            {
                examined = consumed = end;
                return true;
            }
            message = default;
            return false;
        }

        void IMessageWriter<RawFrame>.WriteMessage(RawFrame message, IBufferWriter<byte> output)
            => message.Write(output);
    }
}
