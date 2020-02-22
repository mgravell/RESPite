using System;
using System.Buffers;

namespace Respite
{
    public readonly struct Block<T>
    {
        private readonly T _value;
        private readonly ReadOnlySequence<T> _values;
        public int Count { get; }
        public bool IsEmpty => Count == 0;
        public bool IsSequence => !_isSingle; // so that true for default
        private readonly bool _isSingle;

        public Block(in T value)
        {
            _value = value;
            _values = default;
            Count = 1;
            _isSingle = true;
        }

        public bool TryGetSingle(out T value)
        {
            if (_isSingle)
            {
                value = _value;
                return true;
            }
            else if (TryGetSingleSpan(out var span) && span.Length == 1)
            {
                value = span[0];
                return true;
            }
            value = default!;
            return false;
        }

        public bool TryGetSingleSpan(out ReadOnlySpan<T> span)
        {
            if (IsSequence && _values.IsSingleSegment)
            {
                span = _values.FirstSpan;
                return true;
            }
            span = default;
            return false;
        }

        public Block(ReadOnlyMemory<T> values)
        {
            if (values.IsEmpty)
            {
                this = default;
            }
            else
            {
                Count = values.Length;
                _isSingle = false;
                _values = new ReadOnlySequence<T>(values);
                _value = default!;
            }
        }

        public Block(in ReadOnlySequence<T> values)
        {
            if (values.IsEmpty)
            {
                this = default;
            }
            else
            {
                Count = checked((int)values.Length);
                _isSingle = false;
                _values = values;
                _value = default!;
            }
        }

        public Enumerator GetEnumerator()
        {
            if (_isSingle) return new Enumerator(_value);
            if (TryGetSingleSpan(out var span)) return new Enumerator(span);
            return new Enumerator(_values);
        }

        public ref struct Enumerator
        {
            const int INDEX_SINGLE_PRE = -1;
            private int _nextIndex;
            private ReadOnlySpan<T> _span;
            private ReadOnlySequence<T>.Enumerator _seqEnumerator;
            public T Current { get; private set; }
            public Enumerator(in T value)
            {
                Current = value;
                _nextIndex = INDEX_SINGLE_PRE;
                _seqEnumerator = default;
                _span = default;
            }
            public Enumerator(ReadOnlySpan<T> values)
            {
                Current = default!;
                _nextIndex = 0;
                _seqEnumerator = default;
                _span = values;
            }
            internal Enumerator(in ReadOnlySequence<T> values)
            {
                Current = default!;
                _nextIndex = 0;
                _seqEnumerator = values.GetEnumerator();
                _span = default;
            }

            public bool MoveNext()
            {
                var index = _nextIndex++;
                if (index == INDEX_SINGLE_PRE) return true;
                if (index >= _span.Length) return SequenceMoveNext();
                Current = _span[index];
                return true;
            }

            private bool SequenceMoveNext()
            {
                while (_seqEnumerator.MoveNext())
                {
                    _span = _seqEnumerator.Current.Span;
                    if (!_span.IsEmpty)
                    {
                        _nextIndex = 1;
                        Current = _span[0];
                        return true;
                    }
                }
                Current = default!;
                return false;
            }
        }
    }
}
