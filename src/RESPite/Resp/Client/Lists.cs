using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.Client;

/// <summary>
/// Operations relating to lists.
/// </summary>
public static class Lists
{
    /// <summary>
    /// Returns the length of the list stored at key.
    /// </summary>
    public static readonly RespCommand<SimpleString, long> LLEN = new(Default);

    /// <summary>
    /// Returns the element at index index in the list stored at key. The index is zero-based, so 0 means the first element, 1 the second element and so on. Negative indices can be used to designate elements starting at the tail of the list. Here, -1 means the last element, -2 means the penultimate and so forth.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, int Index), LeasedString> LINDEX = new(Default);

    /// <summary>
    /// Returns the specified elements of the list stored at key. The offsets start and stop are zero-based indexes, with 0 being the first element of the list (the head of the list), 1 being the next element and so on. These offsets can also be negative numbers indicating offsets starting at the end of the list. For example, -1 is the last element of the list, -2 the penultimate, and so on.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, int Start, int Stop), LeasedStrings> LRANGE = new(Default);
}
