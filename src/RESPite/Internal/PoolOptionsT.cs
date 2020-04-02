using Respite.Internal;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Respite
{
    internal sealed class PoolOptions<T>
    {
        internal const int DefaultMinCount = 1, DefaultMaxCount = 50;
        public static PoolOptions<T> Default { get; } = new PoolOptions<T>(DefaultMinCount, DefaultMaxCount, null, null, null);
        public int MinCount { get; }
        public int MaxCount { get; }
        public Func<object?, CancellationToken, ValueTask<T>>? Factory { get; }
        public Func<object?, T, ValueTask>? OnRemoved { get; }
        public Func<object?, T, bool>? OnValidate { get; }

        public static PoolOptions<T> Create(int minCount = DefaultMinCount, int maxCount = DefaultMaxCount,
            Func<object?, CancellationToken, ValueTask<T>>? factory = null,
            Func<object?, T, ValueTask>? onRemoved = null,
            Func<object?, T, bool>? onValidate = null)
            => minCount == DefaultMinCount && maxCount == DefaultMaxCount && factory == null && onRemoved == null && onValidate == null
                ? Default : new PoolOptions<T>(minCount, maxCount, factory, onRemoved, onValidate);

        private PoolOptions(int minCount, int maxCount, Func<object?, CancellationToken, ValueTask<T>>? factory, Func<object?, T, ValueTask>? onRemoved, Func<object?, T, bool>? onValidate)
        {
            if (minCount < 1) ThrowHelper.ArgumentOutOfRange(nameof(minCount));
            if (maxCount < minCount) ThrowHelper.ArgumentOutOfRange(nameof(maxCount));

            MinCount = minCount;
            MaxCount = maxCount;
            Factory = factory;
            OnRemoved = onRemoved;
            OnValidate = onValidate;
        }

        private BoundedChannelOptions? _channelOptions;
        internal Channel<T> CreateChannel()
        {
            _channelOptions ??= new BoundedChannelOptions(MaxCount)
            {
                AllowSynchronousContinuations = false,
                Capacity = MaxCount,
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            };

            return Channel.CreateBounded<T>(_channelOptions);
        }
    }
}
