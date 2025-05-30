﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using RESPite.Messages;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;

namespace RESPite.Resp.Commands;

/// <summary>
/// Represents a RESP command that sends message of type <typeparamref name="TRequest"/>, and
/// receives values of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The type used to represent the parameters of this operation.</typeparam>
/// <typeparam name="TResponse">The type returned by this operation.</typeparam>
public readonly struct RespCommand<TRequest, TResponse>
{
    /// <inheritdoc/>
    public override string? ToString() => writer.ToString();
    internal readonly IRespWriter<TRequest> writer;
    internal readonly IRespReader<Empty, TResponse> reader;

    private RespCommand(IRespWriter<TRequest> writer, IRespReader<Empty, TResponse> reader)
    {
        this.writer = writer;
        this.reader = reader;
    }

    /// <summary>
    /// Change the command associated with this operation.
    /// </summary>
    public RespCommand<TRequest, TResponse> WithAlias(string command)
        => new(writer.WithAlias(command), reader);

    /// <summary>
    /// Change the reader associated with this operation.
    /// </summary>
    public RespCommand<TRequest, TResult> WithReader<TResult>(IRespReader<Empty, TResult> reader)
        => new(writer, reader);

    /// <summary>
    /// Change the reader associated with this operation using the supplied factory.
    /// </summary>
    public RespCommand<TRequest, TResult> WithReader<TResult>(RespCommandFactory factory)
        => new(writer, factory.CreateReader<Empty, TResult>() ?? throw new ArgumentNullException(nameof(factory), $"No suitable reader available for '{typeof(TResult).Name}'"));

    /// <summary>
    /// Create a new command instance.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("This will result in an unconfigured command; instead, pass 'default' to the secondary constructor.", true)]
    public RespCommand() => throw new NotSupportedException();

    /// <summary>
    /// Create a new command instance.
    /// </summary>
    public RespCommand(
        RespCommandFactory factory,
        [CallerMemberName] string command = "",
        IRespWriter<TRequest>? writer = null,
        IRespReader<Empty, TResponse>? reader = null)
    {
        if ((reader is null || writer is null) && factory is null)
        {
            throw new ArgumentNullException(nameof(factory), "A factory must be provided if the reader or writer is omitted");
        }
        this.reader = reader ?? factory.CreateReader<Empty, TResponse>() ?? throw new ArgumentNullException(nameof(reader), $"No suitable reader available for '{typeof(TResponse).Name}'");
        if (writer is null)
        {
            writer = factory.CreateWriter<TRequest>(command);
        }
        else
        {
            writer = writer.WithAlias(command);
        }
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer), $"No suitable writer available for '{typeof(TRequest).Name}'");
    }

    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse})"/>
    public TResponse Send(ISyncMessageTransport transport, in TRequest request)
        => transport.Send<TRequest, TResponse>(in request, writer, reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse}, CancellationToken)"/>
    public ValueTask<TResponse> SendAsync(IAsyncMessageTransport transport, in TRequest request, CancellationToken token = default)
        => transport.SendAsync<TRequest, TResponse>(in request, writer, reader, token);

    /// <summary>
    /// Use the existing command as a template for a stateful operation that takes an addition <typeparamref name="TState"/> input,
    /// and a custom reader that parses the data, returning a <typeparamref name="TResult"/> value.
    /// </summary>
    public StatefulRespCommand<TRequest, TState, TResult> WithState<TState, TResult>(IRespReader<TState, TResult> reader)
        => new(writer, reader);
}
