using System.Buffers;
using RESPite.Internal.Buffers;
using RESPite.Messages;

namespace RESPite.Transports.Internal;

internal sealed class MultiplexedAsyncPayload<TRequest, TResponse>(IReader<TRequest, TResponse> reader, in TRequest request) : MultiplexedAsyncPayloadBase<TRequest, TResponse>(reader)
{
    private readonly TRequest _request = request;
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in _request, in payload);
}

internal sealed class MultiplexedAsyncPayload<TResponse>(IReader<Empty, TResponse> reader) : MultiplexedAsyncPayloadBase<Empty, TResponse>(reader)
{
    protected override TResponse Read(in ReadOnlySequence<byte> payload) => Reader.Read(in Empty.Value, in payload);
}

internal abstract partial class MultiplexedAsyncPayloadBase<TRequest, TResponse>(IReader<TRequest, TResponse> reader) : IMultiplexedPayload
{
    public partial bool IsTaskCompleted { get; }

    private partial void OnComplete(TResponse value);
    private partial void OnFaulted(Exception exception);
    public partial void OnCanceled(CancellationToken token);
    private partial Task<TResponse> GetTask();

#if NETCOREAPP3_0_OR_GREATER
    void IThreadPoolWorkItem.Execute() => SetResultWorkerCallback();
#endif

    private RefCountedBuffer<byte> _payload;

    public void SetResultAndProcessByWorker(in ReadOnlySequence<byte> payload)
    {
        if (!IsTaskCompleted)
        {
            _payload = payload.Retain();
            this.OnActivateWorker();
        }
    }

    public void SetResultWorkerCallback()
    {
        var payload = _payload;
        _payload = default;
        try
        {
            var result = Read(payload.Content);
            OnComplete(result);
        }
        catch (Exception ex)
        {
            OnFaulted(ex);
        }
        finally
        {
            payload.Release();
        }
    }

    public ValueTask<TResponse> ResultAsync(CancellationToken token)
    {
        if (token.CanBeCanceled)
        {
            token.ThrowIfCancellationRequested();
            return ResultWithCancellationAsync(token);
        }
        return new(GetTask());
    }

    private async ValueTask<TResponse> ResultWithCancellationAsync(CancellationToken token)
    {
        using var reg = this.WithCancel(token);
        return await GetTask().ConfigureAwait(false);
    }

    protected IReader<TRequest, TResponse> Reader { get; } = reader;
    protected abstract TResponse Read(in ReadOnlySequence<byte> payload);
}

#if UNSAFE_ACCESSOR

/*
We'll keep this around, but: upon consideration, it doesn't really gain us anything; the TCS-subclass
approach used below has the same allocation footprint, and is less brittle.
*/

internal static class BypassTaskCompletionSource<T> // bypass TCS via UnsafeAccessor
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = nameof(TrySetResult))]
    internal static extern bool TrySetResult(Task<T> task, T result);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    internal static extern Task<T> CreateTask();
}
internal static class BypassTaskCompletionSource
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = nameof(TrySetException))]
    internal static extern bool TrySetException(Task task, object exception);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = nameof(TrySetCanceled))]
    internal static extern bool TrySetCanceled(Task task, CancellationToken token);
}

internal abstract partial class MultiplexedAsyncPayloadBase<TRequest, TResponse>
{
    private readonly Task<TResponse> _task = BypassTaskCompletionSource<TResponse>.CreateTask();
    public partial bool IsTaskCompleted => _task.IsCompleted;

    private partial Task<TResponse> GetTask() => _task;

    private partial void OnComplete(TResponse value) => BypassTaskCompletionSource<TResponse>.TrySetResult(_task, value);
    private partial void OnFaulted(Exception exception) => BypassTaskCompletionSource.TrySetException(_task, exception);
    public partial void OnCanceled(CancellationToken token) => BypassTaskCompletionSource.TrySetCanceled(_task, token);
}
#else
internal abstract partial class MultiplexedAsyncPayloadBase<TRequest, TResponse> : TaskCompletionSource<TResponse>
{
    /*
    Subclass TCS to avoid a separate object for the Task creation.
    Note we could also have used AsyncTaskMethodBuilder<TRequest>, but that adds a TRequest field; inheritance works well enough
    */

    public partial bool IsTaskCompleted => Task.IsCompleted;

    private partial Task<TResponse> GetTask() => Task;

    private partial void OnComplete(TResponse value) => TrySetResult(value);
    private partial void OnFaulted(Exception exception) => TrySetException(exception);
    public partial void OnCanceled(CancellationToken token) => TrySetCanceled(token);
}
#endif
