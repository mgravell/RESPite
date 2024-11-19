using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Messages;

namespace RESPite.Transports.Internal;

internal sealed class MultiplexedTransport<TState>(IByteTransport transport, IFrameScanner<TState> scanner, FrameValidation validateOutbound, CancellationToken lifetime)
        : MultiplexedTransportBase<TState>(transport, scanner, validateOutbound, lifetime), IMultiplexedTransport
{ }

internal sealed class SyncMultiplexedTransport<TState>(ISyncByteTransport transport, IFrameScanner<TState> scanner, FrameValidation validateOutbound, CancellationToken lifetime)
    : MultiplexedTransportBase<TState>(transport, scanner, validateOutbound, lifetime), ISyncMultiplexedTransport
{ }

internal sealed class AsyncMultiplexedTransportTransport<TState>(IAsyncByteTransport transport, IFrameScanner<TState> scanner, FrameValidation validateOutbound, CancellationToken lifetime)
    : MultiplexedTransportBase<TState>(transport, scanner, validateOutbound, lifetime), IAsyncMultiplexedTransport
{ }

internal abstract class MultiplexedPayload
{
#if NET6_0_OR_GREATER
    private static readonly Action<object?, CancellationToken> CancelationCallback = static (state, token) => Unsafe.As<MultiplexedPayload>(state!).Cancel(token);
    public CancellationTokenRegistration WithCancel(CancellationToken token) => token.Register(CancelationCallback, this);
#else
    private static readonly Action<object?> CancelationCallback = static state => Unsafe.As<MultiplexedPayload>(state!).Cancel(CancellationToken.None);
    public CancellationTokenRegistration WithCancel(CancellationToken token) => token.Register(CancelationCallback, this);
#endif
    public abstract void Cancel(CancellationToken token);
    public abstract void SetResult(in ReadOnlySequence<byte> payload);
}
internal abstract class MultiplexedPayloadBase<TRequest, TResponse>(IReader<TRequest, TResponse> reader) : MultiplexedPayload
{
    private readonly TaskCompletionSource<TResponse> _tcs = new();

    protected IReader<TRequest, TResponse> Reader { get; } = reader;
    protected abstract TResponse Read(in ReadOnlySequence<byte> payload);

    public override void Cancel(CancellationToken token) => _tcs.TrySetCanceled(token);

    public override void SetResult(in ReadOnlySequence<byte> payload)
    {
        if (_tcs.Task.IsCompleted) return; // already cancelled

        try
        {
            var result = Read(in payload);
            _tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }

    public TResponse Result() => _tcs.Task.GetAwaiter().GetResult();

    public ValueTask<TResponse> ResultAsync(CancellationToken token)
    {
        if (token.CanBeCanceled)
        {
            token.ThrowIfCancellationRequested();
            return ResultWithCancellationAsync(token);
        }
        return new(_tcs.Task);
    }

    private async ValueTask<TResponse> ResultWithCancellationAsync(CancellationToken token)
    {
        using var reg = WithCancel(token);
        return await _tcs.Task.ConfigureAwait(false);
    }
}
internal sealed class MultiplexedPayload<TRequest, TResponse>(IReader<TRequest, TResponse> reader, in TRequest request) : MultiplexedPayloadBase<TRequest, TResponse>(reader)
{
    private readonly TRequest _request = request;
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in _request, in payload);
}

internal sealed class MultiplexedPayload<TResponse>(IReader<Empty, TResponse> reader) : MultiplexedPayloadBase<Empty, TResponse>(reader)
{
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in Empty.Value, in payload);
}

internal abstract class MultiplexedTransportBase<TState> : IRequestResponseBase
{
    private readonly CancellationToken _lifetime;
    private readonly IByteTransportBase _transport;
    private readonly IFrameScanner<TState> _scanner;
    private readonly int _flags;
    private const int SUPPORT_SYNC = 1 << 0, SUPPORT_ASYNC = 1 << 1, SUPPORT_LIFETIME = 1 << 2, SCAN_OUTBOUND = 1 << 3;

    private readonly Queue<MultiplexedPayload> _pending = new();

    public void Dispose() => (_transport as IDisposable)?.Dispose();

    public ValueTask DisposeAsync() => _transport is IAsyncDisposable d ? d.DisposeAsync() : default;

    public MultiplexedTransportBase(IByteTransportBase transport, IFrameScanner<TState> scanner, FrameValidation validateOutbound, CancellationToken lifetime)
    {
        _lifetime = lifetime;
        _transport = transport;
        _scanner = scanner;
        if (transport is ISyncByteTransport) _flags |= SUPPORT_SYNC;
        if (transport is IAsyncByteTransport) _flags |= SUPPORT_ASYNC;
        if (scanner is IFrameScannerLifetime<TState>) _flags |= SUPPORT_LIFETIME;
#if DEBUG
        if (validateOutbound is FrameValidation.Debug)
        {
            validateOutbound = FrameValidation.Enabled; // always pay the extra in debug
        }
#endif
        if (validateOutbound is FrameValidation.Enabled && scanner is IFrameValidator) _flags |= SCAN_OUTBOUND;
    }

    [DoesNotReturn]
    private void ThrowNotSupported([CallerMemberName] string caller = "") => throw new NotSupportedException(caller);
    private IAsyncByteTransport AsAsync([CallerMemberName] string caller = "")
    {
        if ((_flags & SUPPORT_ASYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<IAsyncByteTransport>(_transport); // type-tested in .ctor
    }

    private ISyncByteTransport AsSync([CallerMemberName] string caller = "")
    {
        if ((_flags & SUPPORT_SYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<ISyncByteTransport>(_transport); // type-tested in .ctor
    }

    private IFrameValidator AsValidator([CallerMemberName] string caller = "")
    {
        if ((_flags & SCAN_OUTBOUND) == 0) ThrowNotSupported(caller);
        return Unsafe.As<IFrameValidator>(_scanner); // type-tested in .ctor
    }

    private IAsyncByteTransport AsAsyncPrechecked() => Unsafe.As<IAsyncByteTransport>(_transport);
    private bool WithLifetime => (_flags & SUPPORT_LIFETIME) != 0;

    private bool ScanOutbound => (_flags & SCAN_OUTBOUND) != 0;

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default)
    {
        _lifetime.ThrowIfCancellationRequested();
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TRequest, TResponse>(reader, in request);

        lock (_pending)
        {
            _pending.Enqueue(payloadToken);
            transport.Write(in content);
        }
        leased.Release();

        return payloadToken.ResultAsync(token);
    }

    private void Validate(in ReadOnlySequence<byte> content)
    {
        try
        {
            if (content.IsEmpty)
            {
                Debug.WriteLine($"{GetType().Name} sending empty frame to transport");
            }
            else
            {
                Debug.WriteLine($"{GetType().Name} sending {content.Length} bytes to transport: {Constants.UTF8.GetString(content)}");
            }
            AsValidator().Validate(in content);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid outbound frame (${content.Length} bytes): {ex.Message}", ex);
        }
    }

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default)
    {
        _lifetime.ThrowIfCancellationRequested();
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TResponse>(reader);

        lock (_pending)
        {
            _pending.Enqueue(payloadToken);
            transport.Write(in content);
        }
        leased.Release();

        return payloadToken.ResultAsync(token);
    }
    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TRequest, TResponse>(reader, in request);

        lock (_pending)
        {
            _pending.Enqueue(payloadToken);
            transport.Write(in content);
        }
        leased.Release();

        return payloadToken.Result();
    }
    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
    {
        _lifetime.ThrowIfCancellationRequested();
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TResponse>(reader);

        lock (_pending)
        {
            _pending.Enqueue(payloadToken);
            transport.Write(in content);
        }
        leased.Release();

        return payloadToken.Result();
    }

    public event MessageCallback? OutOfBandData;
}
