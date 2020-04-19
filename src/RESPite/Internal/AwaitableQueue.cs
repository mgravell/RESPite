using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Respite.Internal
{
    internal sealed class AwaitableQueue<T> : IDisposable where T : class
    {
        private readonly object _syncLock = new object();
        private readonly Queue<T> _contents;
        private readonly Queue<IAwaiter> _awaiters = new Queue<IAwaiter>();
        private readonly int _maxCount;
        private bool _isDisposed;
        public AwaitableQueue(int maxCount)
        {
            _contents = new Queue<T>(maxCount);
            _maxCount = maxCount;
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                _isDisposed = true;
                _contents.Clear();
                while (_awaiters.Count != 0)
                    _awaiters.Dequeue().Cancel();
            }
        }

        public bool TryTake(out T? value)
        {
            lock (_syncLock)
            {
                if (_awaiters.Count == 0 && _contents.Count != 0)
                {
                    value = _contents.Dequeue();
                    return true;
                }
            }
            value = default;
            return false;
        }

        public bool TryReturn(T value)
        {
            if (value == null) return false;
            lock (_syncLock)
            {
                if (_isDisposed) return false;
                while (_awaiters.Count != 0)
                {
                    if (_awaiters.Dequeue().TryProvide(value))
                        return true;
                }
                if (_contents.Count < _maxCount)
                {
                    _contents.Enqueue(value);
                    return true;
                }
            }
            return false;
        }

        public T Take(in TimeSpan timeout)
            => TryTake(out var val) ? val! : TakeSlow(timeout);

        public ValueTask<T> TakeAsync(in CancellationToken cancellationToken)
            => TryTake(out var val) ? new ValueTask<T>(val!) : TakeSlowAsync(cancellationToken);

        private T TakeSlow(in TimeSpan timeout)
        {
            SyncAwaiter awaiter;
            lock (_syncLock)
            {
                if (_isDisposed) ThrowDisposed();
                if (_awaiters.Count == 0 && _contents.Count != 0)
                {
                    return _contents.Dequeue();
                }
                awaiter = SyncAwaiter.Get();
                _awaiters.Enqueue(awaiter);
            }
            return awaiter.WaitOne(timeout);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowDisposed() => ThrowHelper.Disposed(ToString());

        private ValueTask<T> TakeSlowAsync(in CancellationToken cancellationToken)
        {
            AsyncAwaiter awaiter;
            lock (_syncLock)
            {
                if (_isDisposed) ThrowDisposed();
                if (_awaiters.Count == 0 && _contents.Count != 0)
                {
                    return new ValueTask<T>(_contents.Dequeue());
                }
                awaiter = AsyncAwaiter.Get(cancellationToken);
                _awaiters.Enqueue(awaiter);
            }
            return awaiter.Task;
        }


        private interface IAwaiter
        {
            bool TryProvide(T value);
            void Cancel();
        }

        private sealed class SyncAwaiter : IAwaiter
        {
            public static SyncAwaiter Get() => new SyncAwaiter();
            private object? _value;
            public T WaitOne(TimeSpan timeout)
            {
                object? obj;
                lock (this)
                {
                    if (_value == null) Monitor.Wait(this, timeout);
                    obj = _value;
                    _value = AwaitableQueue.SENTINEL_CONSUMED;
                }
                if (obj is null) ThrowHelper.Timeout();
                return (T)obj!;
            }

            public void Cancel() => TryProvide(null!);
            public bool TryProvide(T value)
            {
                lock (this)
                {
                    if (_value == null)
                    {
                        _value = value;
                        Monitor.PulseAll(this);
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class AsyncAwaiter : TaskCompletionSource<T>, IAwaiter
        {
            public static AsyncAwaiter Get(in CancellationToken cancellationToken)
            {
                var obj = new AsyncAwaiter();
                if (cancellationToken.CanBeCanceled)
                {
                    if (cancellationToken.IsCancellationRequested)
                        obj.TrySetCanceled();
                    else
                        cancellationToken.Register(s => ((AsyncAwaiter)s).TrySetCanceled(), obj);
                }
                return obj;
            }
            public AsyncAwaiter() : base(TaskCreationOptions.RunContinuationsAsynchronously) { }
            public bool TryProvide(T value) => TrySetResult(value);
            public void Cancel() => TrySetCanceled();
            public new ValueTask<T> Task => new ValueTask<T>(base.Task);
        }
    }
    static class AwaitableQueue
    {
        internal static readonly object SENTINEL_CONSUMED = new object();
    }
}
