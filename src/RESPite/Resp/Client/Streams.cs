﻿using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.Client;

/// <summary>
/// Operations relating to sorted sets.
/// </summary>
public static class Streams
{
    /// <summary>
    /// Returns the sorted set cardinality (number of elements) of the sorted set stored at key.
    /// </summary>
    public static readonly RespCommand<SimpleString, long> XLEN = new(Default);
}
