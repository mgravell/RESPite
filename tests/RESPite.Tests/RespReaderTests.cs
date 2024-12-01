using System.Buffers;
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
