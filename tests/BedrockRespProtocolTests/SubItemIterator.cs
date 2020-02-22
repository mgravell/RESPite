using Respite;
using Xunit;

namespace BedrockRespProtocolTests
{
    public class SubItemIterator
    {
        static int Count(in RespValue value)
        {
            int count = 0;
            foreach (var item in value.SubItems)
                count++;
            return count;
        }
        [Fact]
        public void DefaultIsEmpty()
        {
            RespValue value = default;
            Assert.Equal(0, value.SubItems.Count);
            Assert.Equal(0, Count(value));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("abc abc abc abc abc abc abc abc")]
        public void StringIsEmpty(string payload)
        {
            RespValue value = payload;
            Assert.Equal(0, value.SubItems.Count);
            Assert.Equal(0, Count(value));
        }

        [Fact]
        public void IntegerIsEmpty()
        {
            RespValue value = 42;
            Assert.Equal(0, value.SubItems.Count);
            Assert.Equal(0, Count(value));
        }

        [Fact]
        public void NullIsEmpty()
        {
            RespValue value = RespValue.Null;
            Assert.Equal(0, value.SubItems.Count);
            Assert.Equal(0, Count(value));
        }

        [Fact]
        public void NullArrayIsEmpty()
        {
            var value = RespValue.CreateAggregate(RespType.Array, (RespValue[])null);
            Assert.Equal(0, value.SubItems.Count);
            Assert.Equal(0, Count(value));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(42)]
        public void Arrays(int count)
        {
            var arr = new RespValue[count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = i;
            var value = RespValue.CreateAggregate(RespType.Array, arr);
            Assert.Equal(count, value.SubItems.Count);
            Assert.Equal(count, Count(value));
        }
    }
}
