using System.Buffers;
using System.Diagnostics;

namespace RESPite.Resp.Readers;

/// <summary>
/// Utility methods for <see cref="RespReader"/>s.
/// </summary>
public static class RespReaderExtensions
{
    /// <summary>
    /// Interpret a scalar value as a <see cref="LeasedString"/> value.
    /// </summary>
    public static LeasedString ReadLeasedString(in this RespReader reader)
    {
        if (reader.TryGetSpan(out var span)) return reader.IsNull ? default : new LeasedString(span);

        var len = reader.ScalarLength();
        var result = new LeasedString(len, out var memory);
        int actual = reader.CopyTo(memory.Span);
        Debug.Assert(actual == len);
        return result;
    }

    /// <summary>
    /// Interpret an aggregate value as a <see cref="LeasedStrings"/> value.
    /// </summary>
    public static LeasedStrings ReadLeasedStrings(in this RespReader reader)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Indicates whether the given value is an byte match.
    /// </summary>
    public static bool Is(in this RespReader reader, in SimpleString value)
    {
        if (value.TryGetBytes(span: out var span))
        {
            return reader.Is(span) & reader.IsNull == value.IsNull;
        }

        var len = value.GetByteCount();
        var oversized = ArrayPool<byte>.Shared.Rent(len);
        var actual = value.CopyTo(oversized);
        Debug.Assert(actual == len);
        var result = reader.Is(new ReadOnlySpan<byte>(oversized, 0, len));
        ArrayPool<byte>.Shared.Return(oversized);
        return result;
    }
}
