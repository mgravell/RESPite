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
    public readonly struct RespPayload(string label, ReadOnlySequence<byte> payload, byte[] expected)
    {
        public override string ToString() => Label;
        public string Label { get; } = label;
        public ReadOnlySequence<byte> PayloadRaw { get; } = payload;
        public int Length { get; } = CheckPayload(payload, expected);
        private static int CheckPayload(in ReadOnlySequence<byte> actual, byte[] expected)
        {
            Assert.Equal(expected.LongLength, actual.Length);
            var pool = ArrayPool<byte>.Shared.Rent(expected.Length);
            actual.CopyTo(pool);
            bool isSame = pool.AsSpan(0, expected.Length).SequenceEqual(expected);
            ArrayPool<byte>.Shared.Return(pool);
            Assert.True(isSame, "Data mismatch");
            return expected.Length;
        }

        public RespReader Reader() => new(PayloadRaw);
    }

    public sealed class RespAttribute(string resp) : DataAttribute
    {
        public string Resp { get; } = resp;

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            foreach (var item in GetVariants(Resp))
            {
                yield return new object[] { item };
            }
        }

        private static IEnumerable<RespPayload> GetVariants(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);

            // all in one
            yield return new("Right-sized", new(bytes), bytes);

            var bigger = new byte[bytes.Length + 4];
            bytes.CopyTo(bigger.AsSpan(2, bytes.Length));
            bigger.AsSpan(0, 2).Fill(0xFF);
            bigger.AsSpan(bytes.Length + 2, 2).Fill(0xFF);

            // all in one, oversized
            yield return new("Oversized", new(bigger, 2, bytes.Length), bytes);

            // two-chunks
            for (int i = 0; i <= bytes.Length; i++)
            {
                int offset = 2 + i;
                var left = new Segment(new ReadOnlyMemory<byte>(bigger, 0, offset), null);
                var right = new Segment(new ReadOnlyMemory<byte>(bigger, offset, bigger.Length - offset), left);
                yield return new($"Split:{i}", new ReadOnlySequence<byte>(left, 2, right, right.Length - 2), bytes);
            }

            // N-chunks
            Segment head = new(new(bytes, 0, 1), null), tail = head;
            for (int i = 1; i < bytes.Length; i++)
            {
                tail = new(new(bytes, i, 1), tail);
            }
            yield return new("Chunk-per-byte", new(head, 0, tail, 1), bytes);
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
    [Theory, Resp("$11\r\nhello world\r\n")]
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

    [Theory, Resp("$0\r\n\r\n")]
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

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n")]
    public void BlobError_ImplicitErrors(RespPayload payload)
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = payload.Reader();
            reader.MoveNext();
        });
        Assert.Equal("SYNTAX invalid syntax", ex.Message);
    }

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n")]
    public void BlobError_Careful(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryReadNext());
        Assert.Equal(RespPrefix.BulkError, reader.Prefix);
        Assert.True(reader.Is("SYNTAX invalid syntax"u8));
        Assert.Equal("SYNTAX invalid syntax", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("=15\r\ntxt:Some string\r\n")]
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
        var iter = reader.AggregateChildren();
        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iter.Value.ReadInt32());
        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iter.Value.ReadInt32());
        Assert.True(iter.MoveNext(RespPrefix.Integer));
        Assert.Equal(3, iter.Value.ReadInt32());
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
            arr[i] = sub.ReadInt32();
        }
        iter.MovePast(out reader);
        reader.DemandEnd();

        Assert.Equal([1, 2, 3], arr);
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
