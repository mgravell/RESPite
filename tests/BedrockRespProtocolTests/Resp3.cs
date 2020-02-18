using Respite;
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
            => Verify(payload, RespValue.Create(expectedType, expectedText), null);

        static void AssertWrite(in RespValue value, string expected, RespVersion version = RespVersion.RESP3)
        {
            var buffer = new ArrayBufferWriter<byte>();
            value.Write(buffer, version);
            Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        [Fact]
        public void UnaryBlobVector() => Verify(@"*1
$1
A", RespValue.CreateAggregate(RespType.Array, RespValue.Create(RespType.BlobString, "A")),
            @"*1
$1
A");
        [Fact]
        public void Nulls() => Verify(@"*3
_
$-1
*-1", RespValue.CreateAggregate(RespType.Array,
            RespValue.Null,
            RespValue.Create(RespType.BlobString, (string)null),
            RespValue.CreateAggregate(RespType.Array, (RespValue[])null)),
            @"*3
$-1
$-1
*-1", @"*3
_
_
_");

        [Fact]
        public void NestedCompisiteVector() => Verify(@"*2
*2
:1
:2
#t", RespValue.CreateAggregate(RespType.Array,
        RespValue.CreateAggregate(RespType.Array,
            RespValue.Create(RespType.Number, 1),
            RespValue.Create(RespType.Number, 2))
        , RespValue.True),@"*2
*2
:1
:2
+t");

        [Fact]
        public void SimpleArray() => Verify(@"*3
:1
:2
:3", RespValue.CreateAggregate(RespType.Array, 1, 2, 3),
            @"*3
:1
:2
:3");

        [Fact]
        public void ComplexNestedArray() => Verify(@"*2
*3
:1
$5
hello
:2
#f", RespValue.CreateAggregate(RespType.Array,
    RespValue.CreateAggregate(RespType.Array, 1, "hello", 2),
    RespValue.False),
            @"*2
*3
:1
$5
hello
:2
+f");

        [Fact]
        public void BasicMap() => Verify(@"%2
+first
:1
+second
:2", RespValue.CreateAggregate(RespType.Map,
      RespValue.Create(RespType.SimpleString, "first"),
      1,
      RespValue.Create(RespType.SimpleString, "second"),
      2),
            @"*4
+first
:1
+second
:2");

        [Fact]
        public void BasicSet() => Verify(@"~5
+orange
+apple
#t
:100
:999", RespValue.CreateAggregate(RespType.Set,
      RespValue.Create(RespType.SimpleString, "orange"),
      RespValue.Create(RespType.SimpleString, "apple"),
      true, 100, 999),
            @"*5
+orange
+apple
+t
:100
:999");

        [Fact]
        public void BasicPush() => Verify(@">4
+pubsub
+message
+somechannel
+this is the message", RespValue.CreateAggregate(RespType.Push,
        RespValue.Create(RespType.SimpleString, "pubsub"),
        RespValue.Create(RespType.SimpleString, "message"),
        RespValue.Create(RespType.SimpleString, "somechannel"),
        RespValue.Create(RespType.SimpleString, "this is the message")),
@"*4
+pubsub
+message
+somechannel
+this is the message");

        private static void Verify(string payload, RespValue expected, string resp2, string resp3Override = null)
        {
            var parsed = Parse(ref payload);
            Assert.True(expected.Equals(parsed), "equality");

            var resp3 = payload;
            if (resp3Override != null)
            {
                NormalizeLineEndingsAndEncode(ref resp3Override);
                resp3 = resp3Override;
            }
            AssertWrite(parsed, resp3, RespVersion.RESP3);
            if (resp2 != null)
            {
                NormalizeLineEndingsAndEncode(ref resp2);
                AssertWrite(parsed, resp2, RespVersion.RESP2);
            }
        }

        static RespValue Parse(ref string payload)
        {
            var input = NormalizeLineEndingsAndEncode(ref payload);
            Assert.True(RespValue.TryParse(input, out var value, out var end, out _));
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
