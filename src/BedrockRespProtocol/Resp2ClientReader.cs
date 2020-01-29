using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;

namespace BedrockRespProtocol
{
    internal sealed class Resp2ClientReader : IMessageReader<RedisFrame>
    {
        internal static Resp2ClientReader Instance { get; } = new Resp2ClientReader();
        private Resp2ClientReader() { }

        bool IMessageReader<RedisFrame>.TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RedisFrame message)
        {
            throw new NotImplementedException();
        }
    }
}
