using System.Diagnostics.CodeAnalysis;

namespace RESPite.Resp.Readers;

public ref partial struct RespReader
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
