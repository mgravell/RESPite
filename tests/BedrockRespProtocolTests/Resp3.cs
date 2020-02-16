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
        [InlineData(@":1234", RespType.Number, "1234")]
        [InlineData(@"_", RespType.Null, "")]
        [InlineData(@",1.23", RespType.Double, "1.23")]
        [InlineData(@",+inf", RespType.Double, "+inf")]
        [InlineData(@",-inf", RespType.Double, "-inf")]
        [InlineData(@"#t", RespType.Boolean, "t")]
        [InlineData(@"#f", RespType.Boolean, "f")]
        [InlineData(@"!21
SYNTAX invalid syntax", RespType.BlobError, "SYNTAX invalid syntax")]
        [InlineData(@"=15
txt:Some string", RespType.VerbatimString, "txt:Some string")]
        [InlineData("(3492890328409238509324850943850943825024385", RespType.BigNumber, "3492890328409238509324850943850943825024385")]
        public void SimpleExamples(string payload, RespType expectedType, string expectedText)
            => Verify(payload, RespValue.Create(expectedType, expectedText));
        
        static void AssertWrite(in RespValue value, string expected, RespVersion version = RespVersion.RESP3)
        {
            var buffer = new ArrayBufferWriter<byte>();
            value.Write(buffer, version);
            Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        [Fact]
        public void UnaryBlobVector() => Verify(@"*1
$1
A", RespValue.CreateAggregate(RespType.Array, RespValue.Create(RespType.BlobString, "A")));

        [Fact]
        public void NestedCompisiteVector() => Verify(@"*2
*2
:1
:2
#t", RespValue.CreateAggregate(RespType.Array,
        RespValue.CreateAggregate(RespType.Array,
            RespValue.Create(RespType.Number, 1),
            RespValue.Create(RespType.Number, 2))
        , RespValue.True));

        [Fact]
        public void SimpleArray() => Verify(@"*3
:1
:2
:3", RespValue.CreateAggregate(RespType.Array, 1, 2, 3));

        [Fact]
        public void ComplexNestedArray() => Verify(@"*2
*3
:1
$5
hello
:2
#f", RespValue.CreateAggregate(RespType.Array,
    RespValue.CreateAggregate(RespType.Array, 1, "hello", 2),
    RespValue.False));

        [Fact]
        public void BasicMap() => Verify(@"%2
+first
:1
+second
:2", RespValue.CreateAggregate(RespType.Map,
      RespValue.Create(RespType.SimpleString, "first"),
      1,
      RespValue.Create(RespType.SimpleString, "second"),
      2));

        [Fact]
        public void BasicSet() => Verify(@"~5
+orange
+apple
#t
:100
:999", RespValue.CreateAggregate(RespType.Set,
      RespValue.Create(RespType.SimpleString, "orange"),
      RespValue.Create(RespType.SimpleString, "apple"),
      true, 100, 999));

        [Fact]
        public void BasicPush() => Verify(@">4
+pubsub
+message
+somechannel
+this is the message", RespValue.CreateAggregate(RespType.Push,
        RespValue.Create(RespType.SimpleString, "pubsub"),
        RespValue.Create(RespType.SimpleString, "message"),
        RespValue.Create(RespType.SimpleString, "somechannel"),
        RespValue.Create(RespType.SimpleString, "this is the message")));

        private static void Verify(string payload, RespValue expected)
        {
            var parsed = Parse(ref payload);
            Assert.Equal(expected, parsed);
            
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
