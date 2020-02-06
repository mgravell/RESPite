using Bedrock.Framework.Infrastructure;
using Bedrock.Framework.Protocols;
using Resp;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;

namespace BedrockRespProtocol
{
    internal sealed class Resp2ClientWriter : IMessageWriter<RedisFrame>, IMessageWriter<RawFrame>
    {
        internal static Resp2ClientWriter Instance { get; } = new Resp2ClientWriter();
        private Resp2ClientWriter() { }

        void IMessageWriter<RedisFrame>.WriteMessage(RedisFrame message, IBufferWriter<byte> output)
        {
            switch(message)
            {
                case RedisArray array: WriteArray(array, output); break;
                case RedisSimpleString simpleString: WriteSimpleString(simpleString, output); break;
                default: throw new NotSupportedException();
            }
        }

        void IMessageWriter<RawFrame>.WriteMessage(RawFrame message, IBufferWriter<byte> output)
            => message.Write(output);


        private static ReadOnlySpan<byte> SimpleStringCommandPrefix => new byte[] { (byte)'*', (byte)'1', (byte)'\r', (byte)'\n', (byte)'$' };
        private static ReadOnlySpan<byte> NewLine => new byte[] { (byte)'\r', (byte)'\n' };

        /* Clients send commands to a Redis server as a RESP Array of Bulk Strings. */
        private static void WriteSimpleString(RedisSimpleString frame, IBufferWriter<byte> output)
        {   // simple commands

            // *1\r\n${bytes}\r\n{payload}\r\n
            var writer = new BufferWriter<IBufferWriter<byte>>(output);
            writer.Write(SimpleStringCommandPrefix);
            writer.WriteNumeric((ulong)frame.PayloadBytes);
            writer.Write(NewLine);

            if (frame.IsBytes(out var bytes))
            {
                writer.Write(bytes);
            }
            else if (frame.IsString(out var s))
            {
                writer.WriteAsciiNoValidation(s);
            }
            else
            {
                JustNope();
            }
            

            writer.Write(NewLine);
            writer.Commit();
            static void JustNope() => throw new NotSupportedException();
        }

        private static void WriteArray(RedisArray frame, IBufferWriter<byte> output)
        {   // varadic commands
            GC.KeepAlive(frame);
            GC.KeepAlive(output);
            throw new NotImplementedException();
        }
    }
}
