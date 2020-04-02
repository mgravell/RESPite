using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Respite.Internal
{
    internal sealed class Pool<T> : IAsyncDisposable where T : class
    {
        public ValueTask DisposeAsync()
        {
            if (_isDisposed) return default;

            _isDisposed = true;
            _available.Writer.TryComplete();
            _inUse.Clear();
            Volatile.Write(ref _count, 0);
            return DiscardAsync((state, item) => false, default);
        }

        private volatile bool _isDisposed;
        private int _count, _waiting, _totalCount;
        private readonly PoolOptions<T> _options;
        private readonly object? _state;
        private readonly ConcurrentDictionary<T, T> _inUse = new ConcurrentDictionary<T, T>(RefComparer.Instance);
        private readonly Channel<T> _available;
        
        public int Count => Volatile.Read(ref _count);
        public int TotalCount => Volatile.Read(ref _totalCount);

        public Pool(PoolOptions<T>? options = null, object? state = null)
        {
            _options = options ?? PoolOptions<T>.Default;
            _available = _options.CreateChannel();
            _state = state;
        }

        static readonly Func<T, object?, ValueTask> s_Return = (value, state) => (state as Pool<T>)?.ReturnAsync(value) ?? default;

        private AsyncLifetime<T> AsLifetime(T value) => new AsyncLifetime<T>(value, s_Return, this);
        public ValueTask<AsyncLifetime<T>> RentAsync(CancellationToken cancellationToken = default)
        {
            var pending = TakeAsync(cancellationToken);
            return pending.IsCompletedSuccessfully
                ? new ValueTask<AsyncLifetime<T>>(AsLifetime(this, pending.Result))
                : Awaited(this, pending);

            static async ValueTask<AsyncLifetime< T >> Awaited(Pool<T> @this, ValueTask<T> pending)
            {
                var item = await pending.ConfigureAwait(false);
                return @this.AsLifetime(item);
            }
            static AsyncLifetime<T> AsLifetime(Pool<T> @this, T value) => new AsyncLifetime<T>(value, s_Return, @this);
        }

        public ValueTask<T> TakeAsync(CancellationToken cancellationToken = default)
            => _available.Reader.TryRead(out var item)
                ? new ValueTask<T>(MarkInUse(item))
                : TakeSlowAsync(cancellationToken);

        private T MarkInUse(T item)
        {
            if (!_isDisposed) _inUse[item] = item;
            return item;
        }

        public ValueTask ReturnAsync(T value, CancellationToken cancellationToken = default)
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
                        if (_available.Writer.TryWrite(value))
                            returned = true;
                    }
                }

                if (!returned) return SurrenderAsync(value, cancellationToken);
            }
            return default;
        }

        private async ValueTask SurrenderAsync(T value, CancellationToken cancellationToken)
        {
            // note we don't decrement _count, because this wasn't valid any more
            var onRemoved = _options.OnRemoved;
            if (onRemoved != null) await onRemoved(_state, value).ConfigureAwait(false);

            // now that we've abandoned something, we might have capacity
            if (Volatile.Read(ref _waiting) != 0 && TryGrow())
            {
                var newItem = await GrowAsync(cancellationToken).ConfigureAwait(false);
                await ReturnAsync(newItem).ConfigureAwait(false);
                await Task.Yield(); // try to let the consumers get hold of the next item
            }
        }

        public ValueTask DiscardAsync(CancellationToken cancellationToken = default)
        {
            var predicate = _options.OnValidate;
            return predicate == null ? default : DiscardAsync(predicate, cancellationToken);
        }

        private async ValueTask DiscardAsync(Func<object?, T, bool> predicate, CancellationToken cancellationToken)
        {
            foreach (var pair in _inUse)
            {
                var item = pair.Key;
                if (!predicate(_state, item) && _inUse.TryRemove(item, out _))
                {
                    Interlocked.Decrement(ref _count);
                    await SurrenderAsync(item, cancellationToken);
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

        private ValueTask<T> TakeSlowAsync(in CancellationToken cancellationToken)
        {
            if (TryGrow()) return GrowAsync(cancellationToken);
            var pending = _available.Reader.ReadAsync(cancellationToken);
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
        private async ValueTask<T> GrowAsync(CancellationToken cancellationToken)
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

        private sealed class RefComparer : IEqualityComparer<T>
        {
            public static readonly RefComparer Instance = new RefComparer();
            private RefComparer() { }

            bool IEqualityComparer<T>.Equals(T x, T y) => ReferenceEquals(x, y);

            int IEqualityComparer<T>.GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
