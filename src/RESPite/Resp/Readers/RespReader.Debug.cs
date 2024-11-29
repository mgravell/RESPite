using System.Diagnostics;

namespace RESPite.Resp.Readers;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
public ref partial struct RespReader
#pragma warning restore CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
{
    internal bool DebugEquals(in RespReader other)
        => _prefix == other._prefix
        && _length == other._length
        && _flags == other._flags
        && _bufferIndex == other._bufferIndex
        && _positionBase == other._positionBase
        && _remainingTailLength == other._remainingTailLength;

    internal new string ToString() => $"{Prefix} ({_flags}); length {_length}, {TotalAvailable} remaining";

#if DEBUG
    internal bool VectorizeDisabled { get; set; }
#endif
}
