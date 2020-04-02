using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Respite
{
    public static class Lifetime
    {
        private static class Cache<T>
        {
            public static readonly Action<Memory<T>, object?> ReturnToArrayPool =
                (_, state) => ArrayPool<T>.Shared.Return((T[])state!);
        }

        public static Lifetime<Memory<T>> RentMemory<T>(int length)
        {
            var arr = ArrayPool<T>.Shared.Rent(length);
            var buffer = new Memory<T>(arr, 0, length);
            return new Lifetime<Memory<T>>(buffer, Cache<T>.ReturnToArrayPool, arr);
        }
        internal static Lifetime<ReadOnlySequence<byte>> RentSequence(int length, out Span<byte> buffer)
        {
            var arr = ArrayPool<byte>.Shared.Rent(length);
            buffer = new Span<byte>(arr, 0, length);
            var seq = new ReadOnlySequence<byte>(arr, 0, length);
            return new Lifetime<ReadOnlySequence<byte>>(seq, (_, state) => ArrayPool<byte>.Shared.Return((byte[])state!), arr);
        }
    }

    public readonly struct Lifetime<T> : IDisposable
    {
        private static readonly Action<T, object?> s_StateAsAction = (value, state) => ((Action<T>?)state)?.Invoke(value);
        public Lifetime(T value, Action<T> onDispose) : this(value, s_StateAsAction, onDispose) { }
        public Lifetime(T value)
        {
            Value = value;
            _state = null;
            _onDispose = null;
        }
        public Lifetime(T value, Action<T, object?>? onDispose, object? state)
        {
            Value = value;
            _state = state;
            _onDispose = onDispose;
        }
        // public Lifetime(T value, Action<object, T> onDispose, object state)
        private readonly Action<T, object?>? _onDispose;
        private readonly object? _state;
        public readonly T Value; // directly exposed to allow ref usage

        public void Dispose() => _onDispose?.Invoke(Value, _state);

        public static explicit operator T(in Lifetime<T> value) => value.Value;
        public static implicit operator Lifetime<T>(T value) => new Lifetime<T>(value);

        internal Lifetime<T> WithValue(T value) => new Lifetime<T>(value, _onDispose, _state);
    }

    public readonly struct AsyncLifetime<T> : IAsyncDisposable
    {
        private static readonly Func<T, object?, ValueTask> s_StateAsAction = (value, state) => ((Func<T, ValueTask>?)state)?.Invoke(value) ?? default;
        public AsyncLifetime(T value, Func<T, ValueTask> onDisposeAsync) : this(value, s_StateAsAction, onDisposeAsync) { }
        public AsyncLifetime(T value)
        {
            Value = value;
            _state = null;
            _onDisposeAsync = null;
        }
        public AsyncLifetime(T value, Func<T, object?, ValueTask>? onDisposeAsync, object? state)
        {
            Value = value;
            _state = state;
            _onDisposeAsync = onDisposeAsync;
        }
        // public Lifetime(T value, Action<object, T> onDispose, object state)
        private readonly Func<T, object?, ValueTask>? _onDisposeAsync;
        private readonly object? _state;
        public readonly T Value; // directly exposed to allow ref usage

        public ValueTask DisposeAsync() => _onDisposeAsync?.Invoke(Value, _state) ?? default;

        public static explicit operator T(in AsyncLifetime<T> value) => value.Value;
        public static implicit operator AsyncLifetime<T>(T value) => new AsyncLifetime<T>(value);

        internal AsyncLifetime<T> WithValue(T value) => new AsyncLifetime<T>(value, _onDisposeAsync, _state);
    }
}
