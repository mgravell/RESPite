using System;
using System.Buffers;

namespace Respite
{
    public readonly struct RespValueItems
    {
        private readonly RespValue _parent;
        internal RespValueItems(in RespValue parent)
        {
            _parent = parent;
        }

        public int Count => _parent.GetSubValueCount();

        public Enumerator GetEnumerator()
        {
            if (_parent.IsUnitAggregate(out var value))
                return new Enumerator(value);
            return new Enumerator(_parent.GetSubValues());
        }
        public ref struct Enumerator
        {
            const int INDEX_SINGLE_PRE = -1;
            private int _nextIndex;
            private ReadOnlySpan<RespValue> _span;
            private ReadOnlySequence<RespValue>.Enumerator _seqEnumerator;
            public RespValue Current { get; private set; }
            internal Enumerator(in RespValue current)
            {
                Current = current;
                _nextIndex = INDEX_SINGLE_PRE;
                _seqEnumerator = default;
                _span = default;
            }
            internal Enumerator(ReadOnlySequence<RespValue> values)
            {
                Current = default;
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
                Current = default;
                return false;
            }
        }
    }
}
