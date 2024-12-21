using System.Collections;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Resp.Readers;

public ref partial struct RespReader
{
    /// <summary>
     /// Reads the sub-elements associated with an aggregate value.
     /// </summary>
    public readonly AggregateEnumerator AggregateChildren() => new(in this);

    /// <summary>
    /// Reads the sub-elements associated with an aggregate value.
    /// </summary>
    public ref struct AggregateEnumerator
    {
        // Note that _reader is the overall reader that can see outside this aggregate, as opposed
        // to Current which is the sub-tree of the current element *only*
        private RespReader _reader;
        private int _remaining;

        /// <summary>
        /// Create a new enumerator for the specified <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader containing the data for this operation.</param>
        public AggregateEnumerator(scoped in RespReader reader)
        {
            reader.DemandAggregate();
            _remaining = reader.IsStreaming ? -1 : reader._length;
            _reader = reader;
            Current = default;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator()"/>
        public readonly AggregateEnumerator GetEnumerator() => this;

        /// <inheritdoc cref="IEnumerator{T}.Current"/>
        public RespReader Current; // this is intentionally a field, because of internal mutability

        /// <summary>
        /// Move to the next child if possible, and move the child element into the next node.
        /// </summary>
        public bool MoveNext(RespPrefix prefix)
        {
            bool result = MoveNext();
            if (result)
            {
                Current.MoveNext(prefix);
            }
            return result;
        }

        /// <inheritdoc cref="IEnumerator.MoveNext()"/>>
        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                Current = default;
                return false;
            }

            // in order to provide access to attributes etc, we want Current to be positioned
            // *before* the next element; for that, we'll take a snapshot before we read
            var before = _reader.Clone();

            _reader.MoveNext();
            if (_remaining > 0)
            {
                // non-streaming, decrement
                _remaining--;
            }
            else if (_reader.Prefix == RespPrefix.StreamTerminator)
            {
                // end of streaming aggregate
                _remaining = 0;
                Current = default;
                return false;
            }

            // move past that sub-tree and trim the "before" state, giving
            // us a scoped reader that is *just* that sub-tree
            _reader.SkipChildren();
            before.TrimTo(_reader.BytesConsumed);
            Current = before;
            return true;
        }

        /// <summary>
        /// Move to the end of this aggregate and export the state of the <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader positioned at the end of the data; this is commonly
        /// used to update a tree reader, to get to the next data after the aggregate.</param>
        public void MovePast(out RespReader reader)
        {
            while (MoveNext()) { }
            reader = _reader;
        }
    }

    internal void TrimTo(long length) => TrimBy(length - BytesConsumed);

    internal void TrimBy(long bytes)
    {
        if (bytes < 0) Throw();

        if (bytes < _remainingTailLength)
        {
            // just cut the tail
            _remainingTailLength -= bytes;
            return;
        }

        // otherwise, we've eaten the entire tail and need to cut the current buffer
        bytes -= _remainingTailLength;
        _tail = null;

        // note we can't cut into the *current* element - only into the region *after* that element
        var remaining = CurrentAvailable - TrailingLength;
        if (bytes > remaining) Throw();

        UnsafeTrimCurrentBy((int)bytes);
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(bytes));
    }
}
