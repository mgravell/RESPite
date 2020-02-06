using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;

namespace BedrockRespProtocol
{
    internal sealed class Resp2ClientReader : IMessageReader<RedisFrame>, IMessageReader<RawFrame>
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

            return (input.First.Span[0]) switch
            {
                (byte)'+' => TryParseSimpleString(input, ref consumed, ref examined, out message),
                _ => throw new NotImplementedException(),
            };
        }

        private bool TryParseSimpleString(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out RedisFrame message)
        {
            var sequenceReader = new SequenceReader<byte>(input);

            if (sequenceReader.TryReadTo(out ReadOnlySequence<byte> payloadPlusPrefix, (byte)'\r')
                && sequenceReader.TryRead(out var n) && n == '\n')
            {
                message = RedisSimpleString.Create(payloadPlusPrefix.Slice(1));
                consumed = examined = sequenceReader.Position;
                return true;
            }
            message = default;
            return false;
        }

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
    }
}
