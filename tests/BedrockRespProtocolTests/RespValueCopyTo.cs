using Respite;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace BedrockRespProtocolTests
{
    public class RespValueCopyTo
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("abc def")]
        [InlineData("abc def ghi jkl mno pqr stu vwx")]
        public void TestStrings(string value)
        {
            RespValue resp = value;
            int len = checked((int)resp.GetByteCount());
            using var lease = Lifetime.RentMemory<byte>(len);
            Assert.Equal(len, resp.CopyTo(lease.Value.Span));
            var result = Encoding.UTF8.GetString(lease.Value.Span);
            Assert.Equal(value ?? "", result);
        }

        [Theory]
        [InlineData(0L, "0")]
        [InlineData(-1L, "-1")]
        [InlineData(1L, "1")]
        [InlineData(-42L, "-42")]
        [InlineData(42L, "42")]
        [InlineData((long)int.MinValue, "-2147483648")]
        [InlineData((long)int.MaxValue, "2147483647")]
        [InlineData(long.MinValue, "-9223372036854775808")]
        [InlineData(long.MaxValue, "9223372036854775807")]
        public void TestInt64(long value, string expected)
        {
            RespValue resp = value;
            int len = checked((int)resp.GetByteCount());
            using var lease = Lifetime.RentMemory<byte>(len);
            Assert.Equal(len, resp.CopyTo(lease.Value.Span));
            var result = Encoding.UTF8.GetString(lease.Value.Span);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0.0, "0")]
        [InlineData(-1.0, "-1")]
        [InlineData(1.0, "1")]
        [InlineData(-42.7, "-42.700000000000003")]
        [InlineData(42.7, "42.700000000000003")]
        [InlineData(-0.000007, "-6.9999999999999999E-06")]
        [InlineData(0.000006, "6.0000000000000002E-06")]
        [InlineData(double.MaxValue, "1.7976931348623157E+308")]
        [InlineData(double.MinValue, "-1.7976931348623157E+308")]
        [InlineData(double.Epsilon, "4.9406564584124654E-324")]
        [InlineData(double.PositiveInfinity, "+inf")]
        [InlineData(double.NegativeInfinity, "-inf")]
        [InlineData((double)long.MinValue, "-9.2233720368547758E+18")]
        [InlineData((double)long.MaxValue, "9.2233720368547758E+18")]
        /* note: needs thought re E; from the specification:
           To just start with . assuming an initial zero is invalid. Exponential format is invalid.
           To completely miss the decimal part, that is, the point followed by other digits, is
           valid, so the number 10 may be returned both using the number or double format
         */
        public void TestDouble(double value, string expected)
        {
            RespValue resp = value;
            int len = checked((int)resp.GetByteCount());
            using var lease = Lifetime.RentMemory<byte>(len);
            Assert.Equal(len, resp.CopyTo(lease.Value.Span));
            var result = Encoding.UTF8.GetString(lease.Value.Span);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, "t")]
        [InlineData(false, "f")]
        public void TestBoolean(bool value, string expected)
        {
            RespValue resp = value;
            int len = checked((int)resp.GetByteCount());
            using var lease = Lifetime.RentMemory<byte>(len);
            Assert.Equal(len, resp.CopyTo(lease.Value.Span));
            var result = Encoding.UTF8.GetString(lease.Value.Span);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TestBytes()
        {
            const int MAX_LENGTH = 800;
            var sourceData = ArrayPool<byte>.Shared.Rent(MAX_LENGTH);
            var target = ArrayPool<byte>.Shared.Rent(MAX_LENGTH + 2);
            try
            {
                var rand = new Random(12345);
                rand.NextBytes(sourceData);
                for(int i = 0; i < MAX_LENGTH; i++)
                {
                    int offset = MAX_LENGTH - i;
                    var seq = new ReadOnlySequence<byte>(sourceData, offset, i);
                    RespValue resp = RespValue.Create(RespType.BlobString, seq);

                    Assert.Equal(i, resp.GetByteCount());
                    var dest = new Span<byte>(target, 1, i);
                    Array.Clear(target, 0, i + 2);
                    Assert.Equal(i, resp.CopyTo(dest));
                    Assert.Equal(0, target[0]);
                    Assert.Equal(0, target[i + 1]);
                    for (int j = 0; j < i; j++)
                        Assert.Equal(sourceData[j + offset], target[j + 1]);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sourceData);
                ArrayPool<byte>.Shared.Return(target);
            }
        }
    }
}
