using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    internal sealed class StreamRespConnection : SimpleRespConnection
    {
        private readonly Stream _stream;
        public StreamRespConnection(Stream stream)
        {
            _stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _stream?.Dispose();
        }

        protected override int Read(Memory<byte> buffer)
            => MemoryMarshal.TryGetArray<byte>(buffer, out var segment)
                ? _stream.Read(segment.Array, segment.Offset, segment.Count)
                : _stream.Read(buffer.Span);

        protected override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => MemoryMarshal.TryGetArray<byte>(buffer, out var segment)
                ? new ValueTask<int>(_stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken))
                : _stream.ReadAsync(buffer, cancellationToken);

        protected override void Write(ReadOnlyMemory<byte> buffer)
        {
            if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
                _stream.Write(segment.Array, segment.Offset, segment.Count);
            else
                _stream.Write(buffer.Span);
        }

        protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => MemoryMarshal.TryGetArray<byte>(buffer, out var segment)
                ? new ValueTask(_stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken))
                : _stream.WriteAsync(buffer, cancellationToken);

        protected override void Flush() => _stream.Flush();

        protected override ValueTask FlushAsync(CancellationToken cancellationToken)
            => new ValueTask(_stream.FlushAsync(cancellationToken));
    }
}
