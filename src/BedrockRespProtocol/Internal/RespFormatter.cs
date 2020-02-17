using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;

namespace BedrockRespProtocol.Internal
{
    internal sealed class RespFormatter : IMessageReader<RespValue>, IMessageWriter<RespValue>
    {
        internal static RespFormatter Instance { get; } = new RespFormatter();
        private RespFormatter() { }
        public RespVersion Version { get; set; } = RespVersion.RESP2;
       
        bool IMessageReader<RespValue>.TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RespValue message)
        {
            if (RespValue.TryParse(input, out message, out var end, out _))
            {
                examined = consumed = end;
                return true;
            }
            message = default;
            return false;
        }

        void IMessageWriter<RespValue>.WriteMessage(RespValue message, IBufferWriter<byte> output)
            => message.Write(output, Version);
    }
}
