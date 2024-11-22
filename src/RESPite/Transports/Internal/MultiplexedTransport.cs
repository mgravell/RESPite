using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

internal abstract partial class MultiplexedTransportBase<TState> : IRequestResponseBase
{
    private readonly CancellationToken _lifetime;
    private readonly IByteTransportBase _transport;
    private readonly IFrameScanner<TState> _scanner;
    private volatile bool _completed; // no more results will be provided; doomed

    private void OnComplete()
    {
        _completed = true;

        while (_awaitingResponse.TryDequeue(out var next))
        {
            next.OnCanceled();
        }
    }

    private readonly int _flags;
    private const int SUPPORT_SYNC = 1 << 0, SUPPORT_ASYNC = 1 << 1, /* SUPPORT_LIFETIME = 1 << 2, */ SCAN_OUTBOUND = 1 << 3;

    private readonly ConcurrentQueue<IMultiplexedPayload> _awaitingResponse = new();

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
                            else if (_awaitingResponse.TryDequeue(out var next))
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
    {
        var leased = writer.Serialize(in request);
        var payloadToken = new MultiplexedAsyncPayload<TRequest, TResponse>(reader, in request, token);
        return SendAsyncCore(payloadToken, leased);
    }

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default)
    {
        var leased = writer.Serialize(in request);
        var payloadToken = new MultiplexedAsyncPayload<TResponse>(reader, token);
        return SendAsyncCore(payloadToken, leased);
    }

    private ValueTask<TResponse> SendAsyncCore<TRequest, TResponse>(MultiplexedAsyncPayloadBase<TRequest, TResponse> payloadToken, in RefCountedBuffer<byte> leased)
    {
        ThrowIfCompleted();
        var content = leased.Content;
        if (ScanOutbound) Validate(in content);

        var transport = AsAsync();

        bool haveBacklogLock = false;
        bool haveWriteLock = false;
        bool startWorker = false;
        try
        {
            // get the queue lock; no IO inside this lock, so fine for sync+async
            Monitor.Enter(_notYetWritten, ref haveBacklogLock);
            if (_notYetWritten.Count == 0)
            {
                // we're at the head of the queue; try to exchange the queue lock
                // for the write lock
                haveWriteLock = _mutex.Wait(0);
                startWorker = !haveWriteLock;
            }

            if (!haveWriteLock)
            {
                // join the queue
                _notYetWritten.Enqueue((payloadToken, leased));
            }
            Monitor.Exit(_notYetWritten);
            haveBacklogLock = false;

            if (haveWriteLock)
            {
                _awaitingResponse.Enqueue(payloadToken);
                var pending = transport.WriteAsync(in content);
                if (!pending.IsCompleted)
                {
                    haveWriteLock = false; // transferring to helper method
                    return SendAsyncCoreAwaitedWrite(pending, payloadToken, leased);
                }

                _mutex.Release();
                haveWriteLock = false;
                leased.Release();
            }
            else if (startWorker)
            {
                StartBacklogWorker();
            }
        }
        finally
        {
            if (haveBacklogLock) Monitor.Exit(_notYetWritten);
            if (haveWriteLock) _mutex.Release();
        }

        return payloadToken.ResultAsync();
    }

    private async ValueTask<TResponse> SendAsyncCoreAwaitedWrite<TRequest, TResponse>(ValueTask writeAsync, MultiplexedAsyncPayloadBase<TRequest, TResponse> payloadToken, RefCountedBuffer<byte> leased)
    {
        try
        {
            await writeAsync.ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
        leased.Release();
        return await payloadToken.ResultAsync().ConfigureAwait(false);
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

    private readonly Queue<(IMultiplexedPayload Payload, RefCountedBuffer<byte> Buffer)> _notYetWritten = new();
    private void StartBacklogWorker()
    {
#if NETCOREAPP3_0_OR_GREATER
        ThreadPool.UnsafeQueueUserWorkItem(this, false);
#else
        ThreadPool.UnsafeQueueUserWorkItem(StartBacklogWorkerCallback, this);
#endif
    }

    private void ExecuteBacklogWorker()
    {
        var transport = AsSync();

        _mutex.Wait();
        try
        {
            while (true)
            {
                (IMultiplexedPayload Payload, RefCountedBuffer<byte> Buffer) pair;
                lock (_notYetWritten)
                {
#if NETCOREAPP2_0_OR_GREATER
                    if (!_notYetWritten.TryDequeue(out pair)) break;
#else
                    if (_notYetWritten.Count == 0) break;
                    pair = _notYetWritten.Dequeue();
#endif
                }
                try
                {
                    _awaitingResponse.Enqueue(pair.Payload);
                    transport.Write(pair.Buffer.Content);
                    pair.Buffer.Release();
                }
                catch (Exception ex)
                {
                    pair.Payload.OnFaulted(ex);
                }
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        var leased = writer.Serialize(in request);
        var payloadToken = MultiplexedSyncPayload<TRequest, TResponse>.Get(reader, in request);
        return SendCore(payloadToken, leased);
    }

    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
    {
        var leased = writer.Serialize(in request);
        var payloadToken = MultiplexedSyncPayload<TResponse>.Get(reader);
        return SendCore(payloadToken, leased);
    }

    private TResponse SendCore<TRequest, TResponse>(MultiplexedSyncPayloadBase<TRequest, TResponse> payloadToken, in RefCountedBuffer<byte> leased)
    {
        ThrowIfCompleted();
        var content = leased.Content;
        if (ScanOutbound) Validate(in content);

        var transport = AsSync();

        bool haveBacklogLock = false;
        bool havePayloadLock = false;
        bool haveWriteLock = false;
        bool startWorker = false;
        TResponse result;
        try
        {
            // get the payload lock; this should be uncontested
            Monitor.TryEnter(payloadToken.SyncLock, 0, ref havePayloadLock);
            if (!havePayloadLock) ThrowUnableToAcquirePayloadLock();

            // get the queue lock; no IO inside this lock, so fine for sync+async
            Monitor.Enter(_notYetWritten, ref haveBacklogLock);
            if (_notYetWritten.Count == 0)
            {
                // we're at the head of the queue; try to exchange the queue lock
                // for the write lock
                haveWriteLock = _mutex.Wait(0);
                startWorker = !haveWriteLock;
            }

            if (!haveWriteLock)
            {
                // join the queue
                _notYetWritten.Enqueue((payloadToken, leased));
            }
            Monitor.Exit(_notYetWritten);
            haveBacklogLock = false;

            if (haveWriteLock)
            {
                _awaitingResponse.Enqueue(payloadToken);
                transport.Write(in content);

                _mutex.Release();
                haveWriteLock = false;
                leased.Release();
            }
            else if (startWorker)
            {
                StartBacklogWorker();
            }

            result = payloadToken.WaitForResponseHoldingSyncLock();
        }
        finally
        {
            if (haveBacklogLock) Monitor.Exit(_notYetWritten);
            if (haveWriteLock) _mutex.Release();
            if (havePayloadLock) Monitor.Exit(payloadToken.SyncLock);
        }
        payloadToken.Recycle(); // only recycle in the success case, after we've released it
        return result;
    }

    private static void ThrowUnableToAcquirePayloadLock() => throw new InvalidOperationException("The payload lock was not immediately available! This is unexpected and very wrong.");

    public event MessageCallback? OutOfBandData;
}

#if NETCOREAPP3_0_OR_GREATER
internal partial class MultiplexedTransportBase<TState> : IThreadPoolWorkItem
{
    void IThreadPoolWorkItem.Execute() => ExecuteBacklogWorker();
}
#else
internal abstract partial class MultiplexedTransportBase<TState>
{
    private static readonly WaitCallback StartBacklogWorkerCallback = state => Unsafe.As<MultiplexedTransportBase<TState>>(state!).ExecuteBacklogWorker();
}
#endif
