using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Respite
{
    internal sealed class PoolOptions<T>
    {
        internal const int DefaultMaxCount = 50;
        public static PoolOptions<T> Default { get; } = new PoolOptions<T>(DefaultMaxCount, null, null, null);
        public int MaxCount { get; }
        public Func<object?, CancellationToken, ValueTask<T>>? Factory { get; }
        public Func<object?, T, ValueTask>? OnRemoved { get; }
        public Func<object?, T, bool>? OnValidate { get; }

        public static PoolOptions<T> Create(int maxCount = DefaultMaxCount,
            Func<object?, CancellationToken, ValueTask<T>>? factory = null,
            Func<object?, T, ValueTask>? onRemoved = null,
            Func<object?, T, bool>? onValidate = null)
            => maxCount == DefaultMaxCount && factory == null && onRemoved == null && onValidate == null
                ? Default : new PoolOptions<T>(maxCount, factory, onRemoved, onValidate);

        private PoolOptions(int maxCount, Func<object?, CancellationToken, ValueTask<T>>? factory, Func<object?, T, ValueTask>? onRemoved, Func<object?, T, bool>? onValidate)
        {
            if (maxCount < 1) ThrowHelper.ArgumentOutOfRange(nameof(maxCount));

            MaxCount = maxCount;
            Factory = factory;
            OnRemoved = onRemoved;
            OnValidate = onValidate;
        }
    }
}
