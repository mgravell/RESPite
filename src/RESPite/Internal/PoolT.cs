using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Respite.Internal
{
    internal sealed class Pool<T> : IDisposable where T : class
    {
        public void Dispose()
        {
            _isDisposed = true;
            _available.Writer.TryComplete();
            _inUse.Clear();
            Volatile.Write(ref _count, 0);
        }

        private volatile bool _isDisposed;
        private int _count;
        private readonly PoolOptions<T> _options;
        private readonly ConcurrentDictionary<T, T> _inUse = new ConcurrentDictionary<T, T>(RefComparer.Instance);
        private readonly Channel<T> _available;
        
        public int Count => Volatile.Read(ref _count);

        public Pool(PoolOptions<T>? options = null)
        {
            _options = options ?? PoolOptions<T>.Default;
            _available = Channel.CreateBounded<T>(new BoundedChannelOptions(_options.MaxCount));
        }

        static readonly Func<T, object?, ValueTask> s_Return = (value, state) => (state as Pool<T>)?.ReturnAsync(value) ?? default;

        public ValueTask<AsyncLifetime<T>> RentAsync(CancellationToken cancellationToken = default)
            => _available.Reader.TryRead(out var item)
            ? new ValueTask<AsyncLifetime<T>>(new AsyncLifetime<T>(item, s_Return, this))
            : SlowRentAsync(cancellationToken);

        private async ValueTask<AsyncLifetime<T>> SlowRentAsync(CancellationToken cancellationToken)
        {
            var item = await TakeAsync(cancellationToken).ConfigureAwait(false);
            return new AsyncLifetime<T>(item, s_Return, this);
        }

        public ValueTask<T> TakeAsync(CancellationToken cancellationToken = default)
            => _available.Reader.TryRead(out var item)
                ? new ValueTask<T>(MarkInUse(item))
                : TakeSlowAsync(cancellationToken);

        private T MarkInUse(T item)
        {
            if (_isDisposed) ThrowHelper.Disposed(ToString());
            _inUse[item] = item;
            return item;
        }

        public ValueTask ReturnAsync(T value)
        {
            if (value != null)
            {
                if (_inUse.TryRemove(value, out _) && _available.Writer.TryWrite(value))
                {
                    // moved back to available, we're fine
                }
                else
                {
                    // note we don't decrement _count, because this wasn't valid any more
                    var onRemoved = _options.OnRemoved;
                    if (onRemoved != null) return onRemoved(value);
                }
            }
            return default;
        }

        public async ValueTask<int> DiscardAsync(Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                ThrowHelper.ArgumentNull(nameof(predicate));
                return 0;
            }
            int count = 0;
            var onRemoved = _options.OnRemoved;
            foreach (var pair in _inUse)
            {
                var item = pair.Key;
                if (predicate(item) && _inUse.TryRemove(item, out _))
                {
                    Interlocked.Decrement(ref _count);

                    // invoke caller-supplied callback if necessary
                    if (onRemoved != null)
                    {
                        await onRemoved(item).ConfigureAwait(false);
                    }
                }
            }
            return count;
        }

        bool TryGrow()
        {
            int count;
            // can we safely increase it without blowing max-count?
            while ((count = Count) < _options.MaxCount)
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
                => @this.MarkInUse(await pending.ConfigureAwait(false));
        }
        private async ValueTask<T> GrowAsync(CancellationToken cancellationToken)
        {
            try
            {
                var factory = _options.Factory;
                T item = factory == null
                    ? Activator.CreateInstance<T>()
                    : await factory(cancellationToken).ConfigureAwait(false);

                if (item is null) throw new InvalidOperationException(ToString() + " factory returned null");
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
