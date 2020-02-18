# RESPite
Low level RESP handling tools for .NET, intended for consumption by other libraries

RESP is the communications protocol used by [Redis](https://redis.io/) (although it is not strictly tied to Redis, and can be used
in a general purpose sense); most typically this means [RESP2](https://redis.io/topics/protocol)
or [RESP3](https://github.com/antirez/RESP3/blob/master/spec.md).

RESPite is a high performance tool for working with RESP. To get started, a `RespConnection` can be constructed over a `Stream`
(typically a `NetworkStream` for socket-IO, or `SslStream` if TLS is involved), or experimental
[Bedrock](https://github.com/davidfowl/BedrockFramework) bindings are also provided.

Once a connection is established, messages can be sent and/or received
(with no assumption that there will be a 1:1 correlation between requests and responses) either synchronously or asynchronously.

For example:

``` c#
using var client = RespConnection.Create(socket);

// send a message to the server (noting that Redis expects all requests to be
// sent as arrays of bulk-strings)
await client.SendAsync(RespValue.CreateAggregate(RespType.Array, "ping", "hello world"));

// wait for a reply
using var reply = await client.ReceiveAsync();
// check that a RESP error message was not received
reply.Value.ThrowIfError();
```

Notice in particular that the returned value from ``Receive[Async]` is not a `RespValue`, but a `Lifetime<RespValue>`; we
use `Lifetime<T>` to indicate that the reply is "live" - it is directly referring to the input buffers as received from the
network ("zero copy"), and so the receiver needs to indicate when they have finished looking at the data, so that the buffers can be
recycled. For this reason, only a single live reply can be retained - additional attempts to call `Receive[Async]` will fail
until the lifetime has been relinquished; however, for convenience, a `.Preserve()` method is available on `RespValue` that
allows it to outlive the network call.

---

It is assumed that a connection will be used by only one concurrent caller, who may perform at most one read and one write
operation at the same time. Any concurrency must be managed externally by a pool or multiplexer layer.

As a throughput example using 20 concurrent clients (each with their own connection)  performing simple operations like above,
with  a pipeline-depth of 20 commands - on a modest desktop we can achieve 360k+ operations per second using Bedrock.
