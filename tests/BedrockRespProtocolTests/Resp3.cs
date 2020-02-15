using Resp;
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
            var parsed = Parse(payload);
            Assert.Equal(expectedType, parsed.Type);
            Assert.Equal(expectedValue, (string)parsed);
        }

        [Fact]
        public void UnaryBlobVector()
        {
            var parsed = Parse(@"*1
$1
A");
            Assert.Equal(RespType.Array, parsed.Type);

            using var lifetime = parsed.GetSubItems();
            var item = lifetime.Value.ToArray().Single(); // lazy but effective
            Assert.Equal(RespType.BlobString, item.Type);
            Assert.Equal("A", item);
        }

        [Fact]
        public void NestedCompisiteVector()
        {
            var parsed = Parse(@"*2
*2
:1
:2
#t");
            Assert.Equal(RespType.Array, parsed.Type);

            using var lifetime = parsed.GetSubItems();
            var items = lifetime.Value.ToArray(); // lazy but effective
            Assert.Equal(2, items.Length);

            Assert.Equal(RespType.Array, items[0].Type);
            using var inner = items[0].GetSubItems();
            var innerItems = inner.Value.ToArray();
            Assert.Equal(2, innerItems.Length);
            Assert.Equal(RespType.Number, innerItems[0].Type);
            Assert.Equal(1, innerItems[0]);
            Assert.Equal(RespType.Number, innerItems[1].Type);
            Assert.Equal(2, innerItems[1]);

            Assert.Equal(RespType.Boolean, items[1].Type);
            Assert.Equal(true, items[1]);
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
