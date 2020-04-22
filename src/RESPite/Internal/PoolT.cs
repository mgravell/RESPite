using PooledAwait;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Respite.Internal
{
    internal sealed class Pool<T> : IAsyncDisposable where T : class
    {
        public ValueTask DisposeAsync()
        {
            if (_isDisposed) return default;

            _isDisposed = true;
            _availableQueue.Dispose();
            _inUse.Clear();
            Volatile.Write(ref _count, 0);
            return DiscardAsync((state, item) => false);
        }

        private volatile bool _isDisposed;
        private int _count, _waiting, _totalCount;
        private readonly PoolOptions<T> _options;
        private readonly object? _state;
        private readonly ConcurrentDictionary<T, T> _inUse = new ConcurrentDictionary<T, T>(RefComparer.Instance);
        private readonly AwaitableQueue<T> _availableQueue;
        
        public int Count => Volatile.Read(ref _count);
        public int TotalCount => Volatile.Read(ref _totalCount);

        public Pool(PoolOptions<T>? options = null, object? state = null)
        {
            _options = options ?? PoolOptions<T>.Default;
            _availableQueue = new AwaitableQueue<T>(_options.MaxCount);
            _state = state;
        }

        static readonly Func<T, object?, ValueTask> s_ReturnAsync = (value, state) => (state as Pool<T>)?.ReturnAsync(value) ?? default;
        static readonly Action<T, object?> s_ReturnSync = (value, state) => (state as Pool<T>)?.Return(value);

        public ValueTask<AsyncLifetime<T>> RentAsync(CancellationToken cancellationToken = default)
        {
            var pending = TakeAsync(cancellationToken);
            return pending.IsCompletedSuccessfully
                ? new ValueTask<AsyncLifetime<T>>(AsLifetime(this, pending.Result))
                : Awaited(this, pending);

            static async PooledValueTask<AsyncLifetime< T >> Awaited(Pool<T> @this, ValueTask<T> pending)
            {
                var item = await pending.ConfigureAwait(false);
                return AsLifetime(@this, item);
            }
            static AsyncLifetime<T> AsLifetime(Pool<T> @this, T value) => new AsyncLifetime<T>(value, s_ReturnAsync, @this);
        }

        public Lifetime<T> Rent() => new Lifetime<T>(Take(), s_ReturnSync, this);

        public bool TryDetach(T value) => _inUse.TryRemove(value, out _);

        public ValueTask<T> TakeAsync(CancellationToken cancellationToken = default)
        {
            return _availableQueue.TryTake(out var item)
                ? new ValueTask<T>(MarkInUse(item!))
                : TakeSlowAsync(cancellationToken);
        }
        public T Take()
        {
            return _availableQueue.TryTake(out var item)
                ? MarkInUse(item!) : TakeSlow();
        }

        private static readonly TimeSpan s_DefaultTimeout = TimeSpan.FromSeconds(20);

        private T MarkInUse(T item)
        {
            if (!_isDisposed) _inUse[item] = item;
            return item;
        }

        public ValueTask ReturnAsync(T value)
        {
            if (value != null)
            {
                bool returned = false;
                if (_inUse.TryRemove(value, out _))
                {
                    // do we need to validate it?
                    var predicate = _options.OnValidate;
                    if (predicate == null || predicate(_state, value))
                    {
                        if (_availableQueue.TryReturn(value))
                            returned = true;
                    }
                }

                if (!returned) return SurrenderAsync(value);
            }
            return default;
        }

        public void Return(T value)
        {
            if (value != null)
            {
                bool returned = false;
                if (_inUse.TryRemove(value, out _))
                {
                    // do we need to validate it?
                    var predicate = _options.OnValidate;
                    if (predicate == null || predicate(_state, value))
                    {
                        if (_availableQueue.TryReturn(value))
                            returned = true;
                    }
                }

                if (!returned) Surrender(value);
            }
        }

        private async PooledValueTask SpawnInBackground(CancellationToken cancellationToken = default)
        {
            T? newItem = null;
            try
            {
                await Task.Yield();
                newItem = await GrowAsync(cancellationToken).ConfigureAwait(false);
                await ReturnAsync(newItem).ConfigureAwait(false);
            }
            catch
            {
                // swallow the exception; we're on the background

                var onRemoved = _options.OnRemoved;
                if (onRemoved is object && newItem is object)
                {
                    try
                    {
                        await onRemoved(_state, newItem).ConfigureAwait(false);
                    }
                    catch { } // more swallowing; best efforts!
                }
            }
        }
        private async PooledValueTask SurrenderAsync(T value)
        {
            // note we don't decrement _count, because this wasn't valid any more
            var onRemoved = _options.OnRemoved;
            if (onRemoved != null) await onRemoved(_state, value).ConfigureAwait(false);

            // now that we've abandoned something, we might have capacity
            if (Volatile.Read(ref _waiting) != 0 && TryGrow())
                _ = SpawnInBackground();
        }

        private void Surrender(T value)
        {
            // note we don't decrement _count, because this wasn't valid any more
            var onRemoved = _options.OnRemoved;
            if (onRemoved != null) onRemoved(_state, value).AsTask().Wait();

            // now that we've abandoned something, we might have capacity
            if (Volatile.Read(ref _waiting) != 0 && TryGrow())
                _ = SpawnInBackground();
        }

        public ValueTask DiscardAsync()
        {
            var predicate = _options.OnValidate;
            return predicate == null ? default : DiscardAsync(predicate);
        }

        private async PooledValueTask DiscardAsync(Func<object?, T, bool> predicate)
        {
            foreach (var pair in _inUse)
            {
                var item = pair.Key;
                if (!predicate(_state, item) && _inUse.TryRemove(item, out _))
                {
                    Interlocked.Decrement(ref _count);
                    await SurrenderAsync(item);
                }
            }
        }

        bool TryGrow()
        {
            int count;
            // can we safely increase it without blowing max-count?
            while (!_isDisposed & (count = Count) < _options.MaxCount)
            {
                if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
                    return true;
                // if we fail, retry from start
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ValueTask<T> TakeSlowAsync(in CancellationToken cancellationToken)
        {
            if (TryGrow()) return GrowAsync(cancellationToken);
            var pending = _availableQueue.TakeAsync(cancellationToken);
            return pending.IsCompletedSuccessfully
                ? new ValueTask<T>(MarkInUse(pending.Result))
                : Awaited(this, pending);

            async static ValueTask<T> Awaited(Pool<T> @this, ValueTask<T> pending)
            {
                T value;
                try
                {
                    Interlocked.Increment(ref @this._waiting);
                    value = await pending.ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref @this._waiting);
                }
                return @this.MarkInUse(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private T TakeSlow()
        {
            if (TryGrow()) return Grow();
            return MarkInUse(_availableQueue.Take(s_DefaultTimeout));
        }

        private async PooledValueTask<T> GrowAsync(CancellationToken cancellationToken)
        {
            try
            {
                var factory = _options.Factory;
                T item = factory == null
                    ? Activator.CreateInstance<T>()
                    : await factory(_state, cancellationToken).ConfigureAwait(false);

                if (item is null) throw new InvalidOperationException(ToString() + " factory returned null");
                Interlocked.Increment(ref _totalCount);
                return MarkInUse(item);
            }
            catch
            {
                Interlocked.Decrement(ref _count);
                throw;
            }
        }

        private T Grow()
        {
            try
            {
                var factory = _options.Factory;
                T item;
                if (factory == null)
                {
                    item = Activator.CreateInstance<T>();
                }
                else
                {
                    var vt = factory(_state, default);
                    item = vt.IsCompletedSuccessfully ? vt.Result
                        : vt.AsTask().Result;
                    if (item is null) throw new InvalidOperationException(ToString() + " factory returned null");
                }
                Interlocked.Increment(ref _totalCount);
                return MarkInUse(item);
            }
            catch
            {
                Interlocked.Decrement(ref _count);
                throw;
            }
        }

        private sealed class RefComparer : IEqualityComparer<T>
        {
            public static readonly RefComparer Instance = new RefComparer();
            private RefComparer() { }

            bool IEqualityComparer<T>.Equals(T x, T y) => ReferenceEquals(x, y);

            int IEqualityComparer<T>.GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
