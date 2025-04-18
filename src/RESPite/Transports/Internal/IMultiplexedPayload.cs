﻿using System.Buffers;
using System.Runtime.CompilerServices;

namespace RESPite.Transports.Internal;

/*
When multiplexing, we know we'll be going async; we need something to manage the synchronization of that;
for this, we have 2 fundamental types - MultiplexedAsyncPayloadBase and MultiplexedSyncPayloadBase;
the first uses Task for completion, the second uses Monitor.

Additionally, since readers may or may not require the request value, we have four types:

MultiplexedAsyncPayloadBase<TRequest, TResponse>
    MultiplexedAsyncPayload<TResponse> (TRequest===Empty)
    MultiplexedAsyncPayload<TRequest, TResponse>
MultiplexedSyncPayloadBase<TRequest, TResponse>
    MultiplexedSyncPayload<TResponse> (TRequest===Empty)
    MultiplexedSyncPayload<TRequest, TResponse>

To deal with this, IMultiplexedPayload unifies these types, and MultiplexedPayloadExtensions
provides the common code required for async activation.

*/

internal interface IMultiplexedPayload
#if NETCOREAPP3_0_OR_GREATER
    : IThreadPoolWorkItem
#endif
{
    void SetResultAndProcessByWorker(in ReadOnlySequence<byte> payload);
    void OnCanceled();
    void SetResultWorkerCallback();
    void OnFaulted(Exception fault);
}

internal static class MultiplexedPayloadExtensions
{
    internal static readonly Action<object?> CancelationCallback = static state => Unsafe.As<IMultiplexedPayload>(state!).OnCanceled();

#if NETCOREAPP3_0_OR_GREATER
    internal static void OnActivateWorker(this IMultiplexedPayload obj) => ThreadPool.UnsafeQueueUserWorkItem(obj, false);

#else
    private static readonly WaitCallback __activateWorker = static state => Unsafe.As<IMultiplexedPayload>(state!).SetResultWorkerCallback();
    internal static void OnActivateWorker(this IMultiplexedPayload obj) => ThreadPool.UnsafeQueueUserWorkItem(__activateWorker, obj);
#endif
}
