using System;

namespace Respite
{
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
}
