﻿using System.Buffers;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Internal.Buffers;
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

internal abstract partial class MultiplexedPayload
{
#if NET6_0_OR_GREATER
    private static readonly Action<object?, CancellationToken> CancelationCallback = static (state, token) => Unsafe.As<MultiplexedPayload>(state!).Cancel(token);
    public CancellationTokenRegistration WithCancel(CancellationToken token) => token.Register(CancelationCallback, this);
#else
    private static readonly Action<object?> CancelationCallback = static state => Unsafe.As<MultiplexedPayload>(state!).Cancel(CancellationToken.None);
    public CancellationTokenRegistration WithCancel(CancellationToken token) => token.Register(CancelationCallback, this);
#endif
    public abstract void Cancel(CancellationToken token);

    private RefCountedBuffer<byte> _payload;

    protected abstract bool IsCompleted { get; }

    private void SetResultWorkerCallback()
    {
        var payload = _payload;
        _payload = default;
        SetResult(payload.Content);
        payload.Release();
    }

    protected abstract void SetResult(in ReadOnlySequence<byte> payload);

    public void SetResultAndProcessByWorker(in ReadOnlySequence<byte> payload)
    {
        if (!IsCompleted)
        {
            _payload = payload.Retain();
            OnActivateWorker();
        }
    }

    private partial void OnActivateWorker();
}

#if NETCOREAPP3_0_OR_GREATER
internal partial class MultiplexedPayload : IThreadPoolWorkItem
{
    private partial void OnActivateWorker() => ThreadPool.UnsafeQueueUserWorkItem(this, false);
    void IThreadPoolWorkItem.Execute() => SetResultWorkerCallback();
}
#else
internal partial class MultiplexedPayload
{
    private static readonly WaitCallback ActivateWorker = static state => Unsafe.As<MultiplexedPayload>(state!).SetResultWorkerCallback();
    private partial void OnActivateWorker() => ThreadPool.UnsafeQueueUserWorkItem(ActivateWorker, this);
}
#endif

internal abstract partial class MultiplexedPayloadBase<TRequest, TResponse>(IReader<TRequest, TResponse> reader) : MultiplexedPayload
{
    private readonly TaskCompletionSource<TResponse> _tcs = new();

    protected override bool IsCompleted => _tcs.Task.IsCompleted;

    protected IReader<TRequest, TResponse> Reader { get; } = reader;
    protected abstract TResponse Read(in ReadOnlySequence<byte> payload);

    public override void Cancel(CancellationToken token) => _tcs.TrySetCanceled(token);

    protected override void SetResult(in ReadOnlySequence<byte> payload)
    {
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
    private volatile bool _completed; // no more results will be provided; doomed

    private void OnComplete()
    {
        _completed = true;

        // blame the overall lifetime if triggered, otherwise: no fault
        var blame = _lifetime;
        if (!blame.IsCancellationRequested) blame = CancellationToken.None;

        while (TryGetPending(out var next))
        {
            next.Cancel(blame);
        }
    }

    private bool TryGetPending([NotNullWhen(true)] out MultiplexedPayload? payload)
    {
        _mutex.Wait();
        try
        {
#if NETCOREAPP2_0_OR_GREATER
            return _pendingItems.TryDequeue(out payload);
#else
            if (_pendingItems.Count == 0)
            {
                payload = null;
                return false;
            }
            payload = _pendingItems.Dequeue();
            return true;
#endif
        }
        finally
        {
            _mutex.Release();
        }
    }

    private readonly int _flags;
    private const int SUPPORT_SYNC = 1 << 0, SUPPORT_ASYNC = 1 << 1, /* SUPPORT_LIFETIME = 1 << 2, */ SCAN_OUTBOUND = 1 << 3;

    private readonly Queue<MultiplexedPayload> _pendingItems = new();

    private readonly SemaphoreSlim _mutex = new(1, 1);

    public void Dispose() => (_transport as IDisposable)?.Dispose();

    public ValueTask DisposeAsync() => _transport is IAsyncDisposable d ? d.DisposeAsync() : default;

    private void ThrowIfCompleted()
    {
        if (_completed) ThrowCompleted();
        _lifetime.ThrowIfCancellationRequested();

        static void ThrowCompleted() => throw new InvalidOperationException("This transport has already terminated; no further replies will be received");
    }

    internal async Task ReadAllAsync()
    {
        _ = AsAsync(); // checks allowed; AsAsyncPrechecked is now legal
        TState? scanState;
        {
            if (_transport is IFrameScannerLifetime<TState> lifetime)
            {
                lifetime.OnInitialize(out scanState);
            }
            else
            {
                scanState = default;
            }
        }

        try
        {
            while (true) // successive frames
            {
                FrameScanInfo scanInfo = default;
                _scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                while (true) // incremental read of single frame
                {
                    // we can pass partial fragments to an incremental scanner, but we need the entire fragment
                    // for deframe; as such, "skip" is our progress into the current frame for an incremental scanner
                    var entireBuffer = _transport.GetBuffer();
                    var workingBuffer = scanInfo.BytesRead == 0 ? entireBuffer : entireBuffer.Slice(scanInfo.BytesRead);
                    var status = workingBuffer.IsEmpty ? OperationStatus.NeedMoreData : _scanner.TryRead(ref scanState, in workingBuffer, ref scanInfo);
                    switch (status)
                    {
                        case OperationStatus.InvalidData:
                            // we always call advance as a courtesy for backends that need per-read advance
                            _transport.Advance(0);
                            TransportExtensions.ThrowInvalidData();
                            break;
                        case OperationStatus.NeedMoreData:
                            _transport.Advance(0);

                            if (!await AsAsyncPrechecked().TryReadAsync(Math.Max(scanInfo.ReadHint, 1), _lifetime))
                            {
                                if (_transport.GetBuffer().IsEmpty)
                                {
                                    return; // clean exit
                                }
                                TransportExtensions.ThrowEOF(); // partial frame
                            }
                            continue;
                        case OperationStatus.Done when scanInfo.BytesRead <= 0:
                            // if we're not making progress, we'd loop forever
                            _transport.Advance(0);
                            TransportExtensions.ThrowEmptyFrame();
                            break;
                        case OperationStatus.Done:
                            long bytesRead = scanInfo.BytesRead; // snapshot for our final advance
                            workingBuffer = entireBuffer.Slice(0, bytesRead); // includes head and trail data
                            _scanner.Trim(ref scanState, ref workingBuffer, ref scanInfo); // contains just the payload
                            if (scanInfo.IsOutOfBand)
                            {
                                OutOfBandData?.Invoke(workingBuffer);
                            }
                            else if (TryGetPending(out var next))
                            {
                                next.SetResultAndProcessByWorker(in workingBuffer);
                            }
                            else
                            {
                                _transport.Advance(0);
                                TransportExtensions.ThrowNotExpected();
                            }

                            // prepare for next frame
                            _transport.Advance(bytesRead);
                            scanInfo = default;
                            _scanner.OnBeforeFrame(ref scanState, ref scanInfo);
                            continue;
                        default:
                            _transport.Advance(0);
                            TransportExtensions.ThrowInvalidOperationStatus(status);
                            break;
                    }
                }
            }
        }
        finally
        {
            OnComplete();
            if (_transport is IFrameScannerLifetime<TState> lifetime)
            {
                lifetime?.OnComplete(ref scanState);
            }
        }
    }

    public MultiplexedTransportBase(IByteTransportBase transport, IFrameScanner<TState> scanner, FrameValidation validateOutbound, CancellationToken lifetime)
    {
        _lifetime = lifetime;
        _transport = transport;
        _scanner = scanner;
        if (transport is ISyncByteTransport) _flags |= SUPPORT_SYNC;
        if (transport is IAsyncByteTransport) _flags |= SUPPORT_ASYNC;
        /* if (scanner is IFrameScannerLifetime<TState>) _flags |= SUPPORT_LIFETIME; */
#if DEBUG
        if (validateOutbound is FrameValidation.Debug)
        {
            validateOutbound = FrameValidation.Enabled; // always pay the extra in debug
        }
#endif
        if (validateOutbound is FrameValidation.Enabled && scanner is IFrameValidator) _flags |= SCAN_OUTBOUND;

        if ((_flags & SUPPORT_ASYNC) != 0)
        {
            _ = Task.Run(() => ReadAllAsync());
        }
        else
        {
            throw new NotSupportedException("Synchronous multiplexed read not yet implemented");
        }
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

    private bool ScanOutbound => (_flags & SCAN_OUTBOUND) != 0;

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default)
        => SendAsyncCore<TRequest, TResponse>(request, writer, reader, token);
    private async ValueTask<TResponse> SendAsyncCore<TRequest, TResponse>(TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token)
    {
        ThrowIfCompleted();
        var transport = AsAsync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TRequest, TResponse>(reader, in request);

        if (!_mutex.Wait(0))
        {
            await _mutex.WaitAsync(token).ConfigureAwait(false);
        }
        try
        {
            ThrowIfCompleted();
            _pendingItems.Enqueue(payloadToken);
            await transport.WriteAsync(in content, _lifetime).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
        leased.Release();

        return await payloadToken.ResultAsync(token).ConfigureAwait(false);
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
        => SendAsyncCore<TRequest, TResponse>(request, writer, reader, token);

    private async ValueTask<TResponse> SendAsyncCore<TRequest, TResponse>(TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token)
    {
        _lifetime.ThrowIfCancellationRequested();
        var transport = AsAsync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TResponse>(reader);

        if (!_mutex.Wait(0))
        {
            await _mutex.WaitAsync(token).ConfigureAwait(false);
        }
        try
        {
            _pendingItems.Enqueue(payloadToken);
            await transport.WriteAsync(in content, _lifetime).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
        leased.Release();

        return await payloadToken.ResultAsync(token).ConfigureAwait(false);
    }
    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var payloadToken = new MultiplexedPayload<TRequest, TResponse>(reader, in request);

        _mutex.Wait();
        try
        {
            _pendingItems.Enqueue(payloadToken);
            transport.Write(in content);
        }
        finally
        {
            _mutex.Release();
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

        _mutex.Wait();
        try
        {
            _pendingItems.Enqueue(payloadToken);
            transport.Write(in content);
        }
        finally
        {
            _mutex.Release();
        }
        leased.Release();

        return payloadToken.Result();
    }

    public event MessageCallback? OutOfBandData;
}