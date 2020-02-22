using System;
using System.Buffers;

namespace Respite
{
    public readonly struct Block<T>
    {
        private readonly T _value;
        private readonly ReadOnlySequence<T> _values;
        public Block(in T value)
        {
            _value = value;
            _values = default;
            Count = 1;
        }
        public Block(ReadOnlyMemory<T> values)
        {
            Count = values.Length;
            switch(Count)
            {
                case 0:
                    _values = default;
                    _value = default!;
                    break;
                case 1:
                    _values = default;
                    _value = values.Span[0];
                    break;
                default:
                    _values = new ReadOnlySequence<T>(values);
                    _value = default!;
                    break;
            }
        }

        public Block(ReadOnlySequence<T> values)
        {
            if (values.IsSingleSegment)
            {
                this = new Block<T>(values.First);
            }
            else
            {
                Count = checked((int)values.Length);
                _values = values;
                _value = default!;
            }
        }

        public int Count { get; }

        public Enumerator GetEnumerator()
        {
            switch(Count)
            {
                case 0: return default;
                case 1: return new Enumerator(_value);
                default:
                    return _values.IsSingleSegment
                        ? new Enumerator(_values.FirstSpan)
                        : new Enumerator(_values);
            }
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
