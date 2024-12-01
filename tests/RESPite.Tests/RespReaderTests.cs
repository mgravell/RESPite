using System.Buffers;
using RESPite.Resp;
using RESPite.Resp.Readers;

namespace RESPite;

public class RespReaderTests
{
    [Theory]
    [InlineData("$3\r\n128\r\n")]
    public void HandleSplitTokens(string payload)
    {
        var bytes = RespConstants.UTF8.GetBytes(payload).AsMemory();

        for (int i = 4; i <= bytes.Length; i++)
        {
            Segment left = new(bytes.Slice(0, i), null);
            Segment right = new(bytes.Slice(i), left);

            Assert.False(left.IsEmpty);

            ReadOnlySequence<byte> ros =
                right.IsEmpty ? new(left.Memory) : new(left, 0, right, right.Length);

            RespReader reader = new(ros);
            var scan = ScanState.Create(false);
            Assert.True(scan.TryRead(ref reader, out _));
            Assert.True(reader.BytesConsumed == bytes.Length, $"i: {i}, BytesConsumed: {reader.BytesConsumed}");
            Assert.Equal(ros.Length, reader.BytesConsumed);
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
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
