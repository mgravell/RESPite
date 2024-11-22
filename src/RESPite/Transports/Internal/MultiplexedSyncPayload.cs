using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite.Internal.Buffers;
using RESPite.Messages;

namespace RESPite.Transports.Internal;

internal sealed class MultiplexedSyncPayload<TRequest, TResponse>(IReader<TRequest, TResponse> reader, in TRequest request) : MultiplexedSyncPayloadBase<TRequest, TResponse>(reader)
{
    private readonly TRequest _request = request;
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in _request, in payload);
}

internal sealed class MultiplexedSyncPayload<TResponse>(IReader<Empty, TResponse> reader) : MultiplexedSyncPayloadBase<Empty, TResponse>(reader)
{
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in Empty.Value, in payload);
}

internal abstract partial class MultiplexedSyncPayloadBase<TRequest, TResponse> : IMultiplexedPayload
{
    public MultiplexedSyncPayloadBase(IReader<TRequest, TResponse> reader)
    {
        Reader = reader;
        Unsafe.SkipInit(out _result);
    }

    protected IReader<TRequest, TResponse> Reader { get; }

    protected abstract TResponse Read(in ReadOnlySequence<byte> payload);

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
