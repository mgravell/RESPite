using Resp;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
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
        [InlineData(@"$11
hello world", RespType.BlobString, "hello world")]
        [InlineData(@"$0

", RespType.BlobString, "")]
        [InlineData(@"+hello world", RespType.SimpleString, "hello world")]
        [InlineData(@"-ERR this is the error description", RespType.SimpleError, "ERR this is the error description")]
        public void SimpleExamples(string payload, RespType expectedType, string expectedValue)
        {
            var parsed = Parse(ref payload);
            Assert.Equal(expectedType, parsed.Type);
            Assert.Equal(expectedValue, (string)parsed);

            AssertWrite(parsed, payload);
        }

        static void AssertWrite(in RespValue value, string expected)
        {
            var buffer = new ArrayBufferWriter<byte>();
            value.Write(buffer);
            Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        [Fact]
        public void UnaryBlobVector()
        {
            string payload = @"*1
$1
A";
            var parsed = Parse(ref payload);
            Assert.Equal(RespType.Array, parsed.Type);

            Assert.True(parsed.IsUnitAggregate(out var item));
            Assert.Equal(RespType.BlobString, item.Type);
            Assert.Equal("A", item);

            AssertWrite(parsed, payload);
        }

        [Fact]
        public void NestedCompisiteVector()
        {
            string payload = @"*2
*2
:1
:2
#t";
            var parsed = Parse(ref payload);
            Assert.Equal(RespType.Array, parsed.Type);

            var items = parsed.GetSubValues().ToArray(); // lazy but effective
            Assert.Equal(2, items.Length);

            Assert.Equal(RespType.Array, items[0].Type);
            var innerItems = items[0].GetSubValues().ToArray(); // lazy but effective
            Assert.Equal(2, innerItems.Length);
            Assert.Equal(RespType.Number, innerItems[0].Type);
            Assert.Equal(1, innerItems[0]);
            Assert.Equal(RespType.Number, innerItems[1].Type);
            Assert.Equal(2, innerItems[1]);

            Assert.Equal(RespType.Boolean, items[1].Type);
            Assert.Equal(true, items[1]);

            AssertWrite(parsed, payload);
        }

        static RespValue Parse(ref string payload)
        {
            var input = NormalizeLineEndingsAndEncode(ref payload);
            Assert.True(RespValue.TryParse(input, out var value, out var end));
            Assert.True(input.Slice(end).IsEmpty);
            return value;
        }

        static ReadOnlySequence<byte> NormalizeLineEndingsAndEncode(ref string value)
        {
            // this is not very efficient; it does not need to be

            if (string.IsNullOrEmpty(value))
            {
                value = "";
                return default;
            }

            // in case the source is not reliable re \r, \n, \r\n
            var sb = new StringBuilder(value.Length);
            var sr = new StringReader(value);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                sb.Append(line).Append("\r\n");
            }
            value = sb.ToString();
            var arr = Encoding.UTF8.GetBytes(value);
            return new ReadOnlySequence<byte>(arr);
        }
    }
}
