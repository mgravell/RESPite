using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;
using System.Text;

namespace BedrockRespProtocol
{
    internal sealed class Resp2ClientReader : IMessageReader<RedisFrame>
    {
        internal static Resp2ClientReader Instance { get; } = new Resp2ClientReader();
        private Resp2ClientReader() { }

        bool IMessageReader<RedisFrame>.TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RedisFrame message)
        {
            if (input.IsEmpty)
            {
                message = default;
                return false;
            }

            switch (input.First.Span[0])
            {
                case (byte)'+': return TryParseSimpleString(input, ref consumed, ref examined, out message);
                default:
                    throw new NotImplementedException();
            }
        }

        private static ReadOnlySpan<byte> NewLine => new byte[] { (byte)'\r', (byte)'\n' };

        private bool TryParseSimpleString(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RedisFrame message)
        {
            var sequenceReader = new SequenceReader<byte>(input);

            if (sequenceReader.TryReadTo(out ReadOnlySpan<byte> payloadPlusPrefix, NewLine))
            {
                message = RedisSimpleString.Create(payloadPlusPrefix.Slice(1));
                consumed = examined = sequenceReader.Position;
                return true;
            }
            message = default;
            return false;
        }
    }
}
