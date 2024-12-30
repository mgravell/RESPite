using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Readers;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace RESPite;

public class RespReaderTests(ITestOutputHelper logger)
{
    public readonly struct RespPayload(string label, ReadOnlySequence<byte> payload, byte[] expected, bool outOfBand)
    {
        public override string ToString() => Label;
        public string Label { get; } = label;
        public ReadOnlySequence<byte> PayloadRaw { get; } = payload;
        public int Length { get; } = CheckPayload(payload, expected, outOfBand);
        private static int CheckPayload(scoped in ReadOnlySequence<byte> actual, byte[] expected, bool outOfBand)
        {
            Assert.Equal(expected.LongLength, actual.Length);
            var pool = ArrayPool<byte>.Shared.Rent(expected.Length);
            actual.CopyTo(pool);
            bool isSame = pool.AsSpan(0, expected.Length).SequenceEqual(expected);
            ArrayPool<byte>.Shared.Return(pool);
            Assert.True(isSame, "Data mismatch");

            // verify that the data exactly passes frame-scanning
            ScanState state = ScanState.Create(false);
            RespReader reader = new(actual);
            Assert.True(state.TryRead(ref reader, out long bytesRead));
            Assert.Equal(expected.Length, bytesRead);
            Assert.Equal(expected.Length, state.TotalBytes);
            Assert.True(state.IsComplete, nameof(state.IsComplete));
            Assert.Equal(outOfBand, state.IsOutOfBand);
            return expected.Length;
        }

        public RespReader Reader() => new(PayloadRaw);
    }

    public sealed class RespAttribute : DataAttribute
    {
        private readonly object _value;
        public bool OutOfBand { get; init; }
        public RespAttribute(string value) => _value = value;
        public RespAttribute(params string[] values) => _value = values;

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            switch (_value)
            {
                case string s:
                    foreach (var item in GetVariants(s, OutOfBand))
                    {
                        yield return new object[] { item };
                    }
                    break;
                case string[] arr:
                    foreach (string s in arr)
                    {
                        foreach (var item in GetVariants(s, OutOfBand))
                        {
                            yield return new object[] { item };
                        }
                    }
                    break;
            }
        }

        private static IEnumerable<RespPayload> GetVariants(string value, bool outOfBand)
        {
            var bytes = Encoding.UTF8.GetBytes(value);

            // all in one
            yield return new("Right-sized", new(bytes), bytes, outOfBand);

            var bigger = new byte[bytes.Length + 4];
            bytes.CopyTo(bigger.AsSpan(2, bytes.Length));
            bigger.AsSpan(0, 2).Fill(0xFF);
            bigger.AsSpan(bytes.Length + 2, 2).Fill(0xFF);

            // all in one, oversized
            yield return new("Oversized", new(bigger, 2, bytes.Length), bytes, outOfBand);

            // two-chunks
            for (int i = 0; i <= bytes.Length; i++)
            {
                int offset = 2 + i;
                var left = new Segment(new ReadOnlyMemory<byte>(bigger, 0, offset), null);
                var right = new Segment(new ReadOnlyMemory<byte>(bigger, offset, bigger.Length - offset), left);
                yield return new($"Split:{i}", new ReadOnlySequence<byte>(left, 2, right, right.Length - 2), bytes, outOfBand);
            }

            // N-chunks
            Segment head = new(new(bytes, 0, 1), null), tail = head;
            for (int i = 1; i < bytes.Length; i++)
            {
                tail = new(new(bytes, i, 1), tail);
            }
            yield return new("Chunk-per-byte", new(head, 0, tail, 1), bytes, outOfBand);
        }
    }

    [Theory, Resp("$3\r\n128\r\n")]
    public void HandleSplitTokens(RespPayload payload)
    {
        RespReader reader = payload.Reader();
        var scan = ScanState.Create(false);
        bool readResult = scan.TryRead(ref reader, out _);
        logger.WriteLine(scan.ToString());
        Assert.Equal(payload.Length, reader.BytesConsumed);
        Assert.True(readResult);
    }

    // the examples from https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md
    [Theory, Resp("$11\r\nhello world\r\n", "$?\r\n;6\r\nhello \r\n;5\r\nworld\r\n;0\r\n")]
    public void BlobString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
#if NET7_0_OR_GREATER
        Assert.Equal("hello world", reader.ParseChars<string>());
#endif
        /* interestingly, string does not implement IUtf8SpanParsable
#if NET8_0_OR_GREATER
        Assert.Equal("hello world", reader.ParseBytes<string>());
#endif
        */
        reader.DemandEnd();
    }

    [Theory, Resp("$0\r\n\r\n", "$?\r\n;0\r\n")]
    public void EmptyBlobString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is(""u8));
        Assert.Equal("", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("+hello world\r\n")]
    public void SimpleString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.SimpleString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
        reader.DemandEnd();
    }

    [Theory, Resp("-ERR this is the error description\r\n")]
    public void SimpleError_ImplicitErrors(RespPayload payload)
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = payload.Reader();
            reader.MoveNext();
        });
        Assert.Equal("ERR this is the error description", ex.Message);
    }

    [Theory, Resp("-ERR this is the error description\r\n")]
    public void SimpleError_Careful(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryReadNext());
        Assert.Equal(RespPrefix.SimpleError, reader.Prefix);
        Assert.True(reader.Is("ERR this is the error description"u8));
        Assert.Equal("ERR this is the error description", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp(":1234\r\n")]
    public void Number(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("1234"u8));
        Assert.Equal("1234", reader.ReadString());
        Assert.Equal(1234, reader.ReadInt32());
        Assert.Equal(1234D, reader.ReadDouble());
        Assert.Equal(1234M, reader.ReadDecimal());
#if NET7_0_OR_GREATER
        Assert.Equal(1234, reader.ParseChars<int>());
        Assert.Equal(1234D, reader.ParseChars<double>());
        Assert.Equal(1234M, reader.ParseChars<decimal>());
#endif
#if NET8_0_OR_GREATER
        Assert.Equal(1234, reader.ParseBytes<int>());
        Assert.Equal(1234D, reader.ParseBytes<double>());
        Assert.Equal(1234M, reader.ParseBytes<decimal>());
#endif
        reader.DemandEnd();
    }

    [Theory, Resp("_\r\n")]
    public void Null(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Null);
        Assert.True(reader.Is(""u8));
        Assert.Null(reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp(",1.23\r\n")]
    public void Double(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("1.23"u8));
        Assert.Equal("1.23", reader.ReadString());
        Assert.Equal(1.23D, reader.ReadDouble());
        Assert.Equal(1.23M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(":10\r\n")]
    public void Integer_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(",10\r\n")]
    public void Double_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(",inf\r\n")]
    public void Double_Infinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("inf"u8));
        Assert.Equal("inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",+inf\r\n")]
    public void Double_PosInfinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("+inf"u8));
        Assert.Equal("+inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",-inf\r\n")]
    public void Double_NegInfinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("-inf"u8));
        Assert.Equal("-inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsNegativeInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",nan\r\n")]
    public void Double_NaN(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("nan"u8));
        Assert.Equal("nan", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsNaN(val));
        reader.DemandEnd();
    }

    [Theory, Resp("#t\r\n")]
    public void Boolean_T(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Boolean);
        Assert.True(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp("#f\r\n")]
    public void Boolean_F(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Boolean);
        Assert.False(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp(":1\r\n")]
    public void Boolean_1(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp(":0\r\n")]
    public void Boolean_0(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.False(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n", "!?\r\n;6\r\nSYNTAX\r\n;15\r\n invalid syntax\r\n;0\r\n")]
    public void BlobError_ImplicitErrors(RespPayload payload)
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = payload.Reader();
            reader.MoveNext();
        });
        Assert.Equal("SYNTAX invalid syntax", ex.Message);
    }

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n", "!?\r\n;6\r\nSYNTAX\r\n;15\r\n invalid syntax\r\n;0\r\n")]
    public void BlobError_Careful(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryReadNext());
        Assert.Equal(RespPrefix.BulkError, reader.Prefix);
        Assert.True(reader.Is("SYNTAX invalid syntax"u8));
        Assert.Equal("SYNTAX invalid syntax", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("=15\r\ntxt:Some string\r\n", "=?\r\n;4\r\ntxt:\r\n;11\r\nSome string\r\n;0\r\n")]
    public void VerbatimString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.VerbatimString);
        Assert.Equal("Some string", reader.ReadString());
        Assert.Equal("Some string", reader.ReadString(out var prefix));
        Assert.Equal("txt", prefix);

        Assert.Equal("Some string", reader.ReadString(out var prefix2));
        Assert.Same(prefix, prefix2); // check prefix recognized and reuse literal
        reader.DemandEnd();
    }

    [Theory, Resp("(3492890328409238509324850943850943825024385\r\n")]
    public void BigIntegers(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BigInteger);
        Assert.Equal("3492890328409238509324850943850943825024385", reader.ReadString());
#if NET8_0_OR_GREATER
        var actual = reader.ParseChars(chars => BigInteger.Parse(chars, CultureInfo.InvariantCulture));

        var expected = BigInteger.Parse("3492890328409238509324850943850943825024385");
        Assert.Equal(expected, actual);
#endif
    }

    [Theory, Resp("*3\r\n:1\r\n:2\r\n:3\r\n")]
    public void Array(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(3, reader.AggregateLength());
        var iter = reader.AggregateChildren();
        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(3, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext(RespPrefix.Integer));
        iter.MovePast(out reader);
        reader.DemandEnd();

        reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        int[] arr = new int[reader.AggregateLength()];
        int i = 0;
        foreach (var sub in reader.AggregateChildren())
        {
            sub.MoveNext(RespPrefix.Integer);
            arr[i++] = sub.ReadInt32();
            sub.DemandEnd();
        }
        iter.MovePast(out reader);
        reader.DemandEnd();

        Assert.Equal([1, 2, 3], arr);
    }

    [Theory, Resp("*2\r\n*3\r\n:1\r\n$5\r\nhello\r\n:2\r\n#f\r\n")]
    public void NestedArray(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);

        Assert.Equal(2, reader.AggregateLength());

        var iter = reader.AggregateChildren();
        Assert.True(iter.MoveNext(RespPrefix.Array));

        Assert.Equal(3, iter.Value.AggregateLength());
        var subIter = iter.Value.AggregateChildren();
        Assert.True(subIter.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, subIter.Value.ReadInt64());
        subIter.Value.DemandEnd();

        Assert.True(subIter.MoveNext(RespPrefix.BulkString));
        Assert.True(subIter.Value.Is("hello"u8));
        subIter.Value.DemandEnd();

        Assert.True(subIter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, subIter.Value.ReadInt64());
        subIter.Value.DemandEnd();

        Assert.False(subIter.MoveNext());

        Assert.True(iter.MoveNext(RespPrefix.Boolean));
        Assert.False(iter.Value.ReadBoolean());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());
        iter.MovePast(out reader);

        reader.DemandEnd();
    }

    [Theory, Resp("%2\r\n+first\r\n:1\r\n+second\r\n:2\r\n")]
    public void Map(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Map);

        Assert.Equal(4, reader.AggregateLength());

        var iter = reader.AggregateChildren();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("first"));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("second"));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());

        iter.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("~5\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n")]
    public void Set(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Set);

        Assert.Equal(5, reader.AggregateLength());

        var iter = reader.AggregateChildren();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("orange"));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("apple"));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Boolean));
        Assert.True(iter.Value.ReadBoolean());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(100, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(999, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());

        iter.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("|1\r\n+key-popularity\r\n%2\r\n$1\r\na\r\n,0.1923\r\n$1\r\nb\r\n,0.0012\r\n*2\r\n:2039123\r\n:9543892\r\n")]
    public void AttributeRoot(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(2, reader.AggregateLength());
        var iter = reader.AggregateChildren();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2039123, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(9543892, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());
        iter.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("*3\r\n:1\r\n:2\r\n|1\r\n+ttl\r\n:3600\r\n:3\r\n")]
    public void AttributeInner(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(3, reader.AggregateLength());
        var iter = reader.AggregateChildren();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(3, iter.Value.ReadInt32());
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());
        iter.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp(">3\r\n+message\r\n+somechannel\r\n+this is the message\r\n", OutOfBand = true)]
    public void Push(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Push);
        Assert.Equal(3, reader.AggregateLength());
        var iter = reader.AggregateChildren();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("message"u8));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("somechannel"u8));
        iter.Value.DemandEnd();

        Assert.True(iter.MoveNext(RespPrefix.SimpleString));
        Assert.True(iter.Value.Is("this is the message"u8));
        iter.Value.DemandEnd();

        Assert.False(iter.MoveNext());
        iter.MovePast(out reader);
        reader.DemandEnd();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public override string ToString() => RespConstants.UTF8.GetString(Memory.Span)
            .Replace("\r", "\\r").Replace("\n", "\\n");

        public Segment(ReadOnlyMemory<byte> value, Segment? head)
        {
            Memory = value;
            if (head is not null)
            {
                RunningIndex = head.RunningIndex + head.Memory.Length;
                head.Next = this;
            }
        }
        public bool IsEmpty => Memory.IsEmpty;
        public int Length => Memory.Length;
    }
}
