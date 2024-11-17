# RESPite

Low level RESP handling tools for .NET, intended for consumption by other libraries

RESP is the communications protocol used by [Redis](https://redis.io/docs/latest/develop/reference/protocol-spec/) and Redis-like (¿Redish?) servers (Garnet, ValKey, etc),
although it is not strictly tied to Redis, and can be used
in a general purpose sense.

`RESPite` is a high performance tool for working with RESP in .NET languages.

`res-cli` is a set of tools based on `RESPite` for working with RESP at the command-line as a .NET global tool.

Current status: experimental