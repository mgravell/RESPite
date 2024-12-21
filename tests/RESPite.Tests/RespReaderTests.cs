using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Readers;
using Xunit.Abstractions;

namespace RESPite;

public class RespReaderTests(ITestOutputHelper logger)
{
    [Theory]
    [InlineData("$3\r\n128\r\n", 0)]
    [InlineData("$3\r\n128\r\n", 1)]
    [InlineData("$3\r\n128\r\n", 2)]
    [InlineData("$3\r\n128\r\n", 3)]
    [InlineData("$3\r\n128\r\n", 4)]
    [InlineData("$3\r\n128\r\n", 5)]
    [InlineData("$3\r\n128\r\n", 6)]
    [InlineData("$3\r\n128\r\n", 7)]
    [InlineData("$3\r\n128\r\n", 8)]
    [InlineData("$3\r\n128\r\n", 9)]
    public void HandleSplitTokens(string payload, int split)
    {
        var bytes = RespConstants.UTF8.GetBytes(payload).AsMemory();

        Segment left = new(bytes.Slice(0, split), null);
        Segment right = new(bytes.Slice(split), left);

        logger.WriteLine($"left: '{left}', right: '{right}'");
        Assert.Equal(split, left.Length);
        Assert.Equal(bytes.Length - split, right.Length);

        ReadOnlySequence<byte> ros =
            right.IsEmpty ? new(left.Memory) : new(left, 0, right, right.Length);
        Assert.Equal(ros.Length, bytes.Length);

        RespReader reader = new(ros);
        var scan = ScanState.Create(false);
        bool readResult = scan.TryRead(ref reader, out _);
        logger.WriteLine(scan.ToString());
        Assert.Equal(bytes.Length, reader.BytesConsumed);
        Assert.True(readResult);
    }

    // the examples from https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md
    [Fact]
    public void BlobString()
    {
        var reader = new RespReader("$11\r\nhello world\r\n"u8);
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
        reader.DemandEnd();
    }

    [Fact]
    public void EmptyBlobString()
    {
        var reader = new RespReader("$0\r\n\r\n"u8);
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is(""u8));
        Assert.Equal("", reader.ReadString());
        reader.DemandEnd();
    }

    [Fact]
    public void SimpleString()
    {
        var reader = new RespReader("+hello world\r\n"u8);
        reader.MoveNext(RespPrefix.SimpleString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
        reader.DemandEnd();
    }

    [Fact]
    public void SimpleError_ImplicitErrors()
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = new RespReader("-ERR this is the error description\r\n"u8);
            reader.MoveNext();
        });
        Assert.Equal("ERR this is the error description", ex.Message);
    }

    [Fact]
    public void SimpleError_Careful()
    {
        var reader = new RespReader("-ERR this is the error description\r\n"u8);
        Assert.True(reader.TryReadNext());
        Assert.Equal(RespPrefix.SimpleError, reader.Prefix);
        Assert.True(reader.Is("ERR this is the error description"u8));
        Assert.Equal("ERR this is the error description", reader.ReadString());
        reader.DemandEnd();
    }

    [Fact]
    public void Number()
    {
        var reader = new RespReader(":1234\r\n"u8);
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("1234"u8));
        Assert.Equal("1234", reader.ReadString());
        Assert.Equal(1234, reader.ReadInt32());
        Assert.Equal(1234D, reader.ReadDouble());
        Assert.Equal(1234M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Fact]
    public void Null()
    {
        var reader = new RespReader("_\r\n"u8);
        reader.MoveNext(RespPrefix.Null);
        Assert.True(reader.Is(""u8));
        Assert.Null(reader.ReadString());
        reader.DemandEnd();
    }

    [Fact]
    public void Double()
    {
        var reader = new RespReader(",1.23\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("1.23"u8));
        Assert.Equal("1.23", reader.ReadString());
        Assert.Equal(1.23D, reader.ReadDouble());
        Assert.Equal(1.23M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Fact]
    public void Integer_Simple()
    {
        var reader = new RespReader(":10\r\n"u8);
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Fact]
    public void Double_Simple()
    {
        var reader = new RespReader(",10\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Fact]
    public void Double_Infinity()
    {
        var reader = new RespReader(",inf\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("inf"u8));
        Assert.Equal("inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Fact]
    public void Double_PosInfinity()
    {
        var reader = new RespReader(",+inf\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("+inf"u8));
        Assert.Equal("+inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Fact]
    public void Double_NegInfinity()
    {
        var reader = new RespReader(",-inf\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("-inf"u8));
        Assert.Equal("-inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsNegativeInfinity(val));
        reader.DemandEnd();
    }

    [Fact]
    public void Double_NaN()
    {
        var reader = new RespReader(",nan\r\n"u8);
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("nan"u8));
        Assert.Equal("nan", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsNaN(val));
        reader.DemandEnd();
    }

    [Theory]
    [InlineData("#t\r\n", RespPrefix.Boolean, true)]
    [InlineData("#f\r\n", RespPrefix.Boolean, false)]
    [InlineData(":1\r\n", RespPrefix.Integer, true)]
    [InlineData(":0\r\n", RespPrefix.Integer, false)]
    public void Boolean(string value, RespPrefix prefix, bool expected)
    {
        var reader = new RespReader(Encoding.ASCII.GetBytes(value));
        reader.MoveNext(prefix);
        Assert.Equal(expected, reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Fact]
    public void BlobError_ImplicitErrors()
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = new RespReader("!21\r\nSYNTAX invalid syntax\r\n"u8);
            reader.MoveNext();
        });
        Assert.Equal("SYNTAX invalid syntax", ex.Message);
    }

    [Fact]
    public void BlobError_Careful()
    {
        var reader = new RespReader("!21\r\nSYNTAX invalid syntax\r\n"u8);
        Assert.True(reader.TryReadNext());
        Assert.Equal(RespPrefix.BulkError, reader.Prefix);
        Assert.True(reader.Is("SYNTAX invalid syntax"u8));
        Assert.Equal("SYNTAX invalid syntax", reader.ReadString());
        reader.DemandEnd();
    }

    [Fact]
    public void VerbatimString()
    {
        var reader = new RespReader("=15\r\ntxt:Some string\r\n"u8);
        reader.MoveNext(RespPrefix.VerbatimString);
        Assert.Equal("Some string", reader.ReadString());
        Assert.Equal("Some string", reader.ReadString(out var prefix));
        Assert.Equal("txt", prefix);

        Assert.Equal("Some string", reader.ReadString(out var prefix2));
        Assert.Same(prefix, prefix2); // check prefix recognized and reuse literal
        reader.DemandEnd();
    }

    [Fact]
    public void BigIntegers()
    {
        var reader = new RespReader("(3492890328409238509324850943850943825024385\r\n"u8);
        reader.MoveNext(RespPrefix.BigInteger);
        Assert.Equal("3492890328409238509324850943850943825024385", reader.ReadString());
#if NET8_0_OR_GREATER
        var actual = reader.ParseChars(chars => BigInteger.Parse(chars, CultureInfo.InvariantCulture));

        var expected = BigInteger.Parse("3492890328409238509324850943850943825024385");
        Assert.Equal(expected, actual);
#endif
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
