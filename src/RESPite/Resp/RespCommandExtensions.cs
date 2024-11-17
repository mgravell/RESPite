using RESPite.Messages;
using RESPite.Resp.Commands;
using RESPite.Transports;

namespace RESPite.Resp;

/// <summary>
/// Additional methods for <see cref="RespCommand{TRequest, TResponse}"/> values.
/// </summary>
public static class RespCommandExtensions
{
    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse})"/>
    public static TResponse Send<TResponse>(this in RespCommand<Empty, TResponse> command, ISyncMessageTransport transport)
        => transport.Send<Empty, TResponse>(in Empty.Value, command.writer, command.reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse}, CancellationToken)"/>
    public static ValueTask<TResponse> SendAsync<TResponse>(this in RespCommand<Empty, TResponse> command, IAsyncMessageTransport transport, CancellationToken token = default)
        => transport.SendAsync<Empty, TResponse>(in Empty.Value, command.writer, command.reader, token);
}
