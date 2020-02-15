using Resp;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using Xunit;

namespace BedrockRespProtocolTests
{
    public class Resp3
    {
        // https://github.com/antirez/RESP3/blob/master/spec.md
        [Theory]
        [InlineData(@"$1
A", RespType.BlobString, "A")]
        public void SimpleExamples(string payload, RespType type, string value)
        {
            var parsed = Parse(payload);
            Assert.Equal(type, parsed.Type);
            Assert.Equal(parsed, value);
        }

        static RespValue Parse(string payload)
        {
            var input = NormalizeLineEndingsAndEncode(payload);
            Assert.True(RespValue.TryParse(input, out var value, out var end));
            Assert.True(input.Slice(end).IsEmpty);
            return value;
        }

        static ReadOnlySequence<byte> NormalizeLineEndingsAndEncode(string value)
        {
            // this is not very efficient; it does not need to be

            if (string.IsNullOrEmpty(value)) return default;
            // in case the source is not reliable re \r, \n, \r\n
            var sb = new StringBuilder(value.Length);
            var sr = new StringReader(value);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                sb.Append(line).Append("\r\n");
            }
            var arr = Encoding.UTF8.GetBytes(sb.ToString());
            return new ReadOnlySequence<byte>(arr);
        }
    }
}
