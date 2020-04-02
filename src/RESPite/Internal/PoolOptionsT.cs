using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    internal sealed class PoolOptions<T>
    {
        public static PoolOptions<T> Default { get; } = new PoolOptions<T>();
        public int MinCount { get; }
        public int MaxCount { get; }
        public Func<CancellationToken, ValueTask<T>>? Factory { get; }
        public Func<T, ValueTask>? OnRemoved { get; }
        public PoolOptions(int minCount = 1, int maxCount = 50, Func<CancellationToken, ValueTask<T>>? factory = null, Func<T, ValueTask>? onRemoved = null)
        {
            if (minCount < 1) ThrowHelper.ArgumentOutOfRange(nameof(minCount));
            if (maxCount < minCount) ThrowHelper.ArgumentOutOfRange(nameof(maxCount));

            MinCount = minCount;
            MaxCount = maxCount;
            Factory = factory;
            OnRemoved = onRemoved;
        }
    }
}
