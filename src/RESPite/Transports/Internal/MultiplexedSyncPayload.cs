using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite.Internal.Buffers;
using RESPite.Messages;

namespace RESPite.Transports.Internal;

internal sealed class MultiplexedSyncPayload<TRequest, TResponse> : MultiplexedSyncPayloadBase<TRequest, TResponse>
{
    [ThreadStatic]
    private static MultiplexedSyncPayload<TRequest, TResponse>? _spare;

    private MultiplexedSyncPayload()
    {
        Unsafe.SkipInit(out _request);
    }

    private TRequest _request;

    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in _request, in payload);

    public static MultiplexedSyncPayload<TRequest, TResponse> Get(IReader<TRequest, TResponse> reader, in TRequest request)
    {
        var obj = _spare ?? new();
        _spare = null;
        obj.Initialize(reader);
        obj._request = request;
        return obj;
    }

    public void Recycle()
    {
        Reset();
        _spare = this;
    }

    protected override void Reset()
    {
        _request = default!;
        base.Reset();
    }
}

internal sealed class MultiplexedSyncPayload<TResponse> : MultiplexedSyncPayloadBase<Empty, TResponse>
{
    [ThreadStatic]
    private static MultiplexedSyncPayload<TResponse>? _spare;

    private MultiplexedSyncPayload() { }

    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in Empty.Value, in payload);

    public static MultiplexedSyncPayload<TResponse> Get(IReader<Empty, TResponse> reader)
    {
        var obj = _spare ?? new();
        _spare = null;
        obj.Initialize(reader);
        return obj;
    }

    public void Recycle()
    {
        Reset();
        _spare = this;
    }
}

internal abstract partial class MultiplexedSyncPayloadBase<TRequest, TResponse> : IMultiplexedPayload
{
    protected MultiplexedSyncPayloadBase()
    {
        Unsafe.SkipInit(out _reader);
        Unsafe.SkipInit(out _result);
    }

    protected virtual void Reset()
    {
        _result = default!;
        _payload = default;
        _state = STATE_PENDING;
        _fault = null;
    }

    public void Initialize(IReader<TRequest, TResponse> reader) => _reader = reader;

    protected IReader<TRequest, TResponse> Reader => _reader;

    protected abstract TResponse Read(in ReadOnlySequence<byte> payload);

    private IReader<TRequest, TResponse> _reader;
    private TResponse _result;
    private RefCountedBuffer<byte> _payload;
    private int _state; // implicit = STATE_PENDING
    private Exception? _fault;

    private const int STATE_PENDING = 0;
    private const int STATE_COMPLETED = 0;
    private const int STATE_CANCELED = 0;
    private const int STATE_FAULTED = 0;

    public bool IsCompleted => Volatile.Read(ref _state) != STATE_PENDING;
    public bool IsCompletedSuccessfully => Volatile.Read(ref _state) == STATE_COMPLETED;

    public object SyncLock => this; // used for pulsing

    private void SignalCompletionIfPending(int newState)
    {
        if (Interlocked.CompareExchange(ref _state, newState, STATE_PENDING) == STATE_PENDING)
        {
            lock (SyncLock)
            {
                Monitor.Pulse(SyncLock);
            }
        }
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private TResponse ThrowFaultOrTimeout()
    {
        Interlocked.CompareExchange(ref _state, STATE_CANCELED, STATE_PENDING);
        throw Volatile.Read(ref _fault) ?? new TimeoutException();
    }

    public TResponse WaitForResponseHoldingSyncLock()
    {
        Debug.Assert(Monitor.IsEntered(SyncLock), "should already have lock");
        return Monitor.Wait(SyncLock) && IsCompletedSuccessfully ? _result : ThrowFaultOrTimeout();
    }

    public TResponse WaitForResponseHoldingSyncLock(int millisecondsTimeout)
    {
        Debug.Assert(Monitor.IsEntered(SyncLock), "should already have lock");
        return Monitor.Wait(SyncLock, millisecondsTimeout) && IsCompletedSuccessfully ? _result : ThrowFaultOrTimeout();
    }

    public void SetResultAndProcessByWorker(in ReadOnlySequence<byte> payload)
    {
        if (!IsCompleted)
        {
            _payload = payload.Retain();
            this.OnActivateWorker();
        }
    }

#if NETCOREAPP3_0_OR_GREATER
    void IThreadPoolWorkItem.Execute() => SetResultWorkerCallback();
#endif

    public void SetResultWorkerCallback()
    {
        var payload = _payload;
        _payload = default;
        int newState = STATE_CANCELED;
        try
        {
            _result = Read(payload.Content);
            newState = STATE_COMPLETED;
        }
        catch (Exception ex)
        {
            _fault = ex;
            newState = STATE_FAULTED;
        }
        finally
        {
            payload.Release();
            SignalCompletionIfPending(newState);
        }
    }

    void IMultiplexedPayload.OnCanceled(CancellationToken token) => SignalCompletionIfPending(STATE_CANCELED);
}
