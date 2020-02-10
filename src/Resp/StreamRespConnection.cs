using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    internal sealed class StreamRespConnection : SimpleRespConnection
    {
        private readonly Stream _stream;
        public StreamRespConnection(Stream stream)
        {
            _stream = stream;
        }

        protected override void Flush(in ReadOnlySequence<byte> payload)
        {
            if (payload.IsSingleSegment)
            {
                Write(_stream, payload.First);
            }
            else
            {
                foreach (var segment in payload)
                {
                    Write(_stream, segment);
                }
            }
            _stream.Flush();

            static void Write(Stream stream, ReadOnlyMemory<byte> buffer)
            {
                if (MemoryMarshal.TryGetArray(buffer, out var segment))
                {   // acknowledge that not all streams are optimized for this
                    stream.Write(segment.Array, segment.Offset, segment.Count);
                }
                else
                {
                    stream.Write(buffer.Span);
                }
            }
        }


        protected override ValueTask FlushAsync(ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            if (payload.IsSingleSegment)
            {
                var write = WriteAsync(_stream, payload.First, cancellationToken);
                if (!write.IsCompletedSuccessfully) return AwaitedWrite(_stream, write, cancellationToken);
                return new ValueTask(_stream.FlushAsync(cancellationToken));
            }
            else
            {
                return FlushSlowAsync(_stream, payload, cancellationToken);
            }
            static async ValueTask FlushSlowAsync(Stream stream, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
            {
                foreach (var segment in payload)
                {
                    await WriteAsync(stream, segment, cancellationToken).ConfigureAwait(false);
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            static ValueTask WriteAsync(Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                if (MemoryMarshal.TryGetArray(buffer, out var segment))
                {   // acknowledge that not all streams are optimized for this
                    return new ValueTask(stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken));
                }
                else
                {
                    return stream.WriteAsync(buffer, cancellationToken);
                }
            }

            static async ValueTask AwaitedWrite(Stream stream, ValueTask write, CancellationToken cancellationToken)
            {
                await write.ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        const int READ_BUFFER_SIZE = 512;
        protected override bool ReadMore()
        {
            var writer = InputWriter;
            var memory = writer.GetMemory(READ_BUFFER_SIZE);
            // acknowledge that not all streams are optimized for this
            var bytes = MemoryMarshal.TryGetArray<byte>(memory, out var segment)
                ? _stream.Read(segment.Array, segment.Offset, segment.Count)
                : _stream.Read(memory.Span);
            bool result = Complete(writer, bytes);
            FlushSync(writer);
            return result;
        }
        static bool Complete(PipeWriter writer, int bytes)
        {
            if (bytes > 0)
            {
                writer.Advance(bytes);
                return true;
            }
            writer.Advance(0);
            return false;
        }
        protected override ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
        {
            var writer = InputWriter;
            var memory = writer.GetMemory(READ_BUFFER_SIZE);
            // acknowledge that not all streams are optimized for this
            var pending = MemoryMarshal.TryGetArray<byte>(memory, out var segment)
                ? new ValueTask<int>(_stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken))
                : _stream.ReadAsync(memory, cancellationToken);

            if (!pending.IsCompletedSuccessfully) return Awaited(writer, pending, cancellationToken);

            bool result = Complete(writer, pending.Result);
            var flush = writer.FlushAsync(cancellationToken);
            if (!flush.IsCompletedSuccessfully) return AwaitedFlush(flush, result);
            return new ValueTask<bool>(result);

            static async ValueTask<bool> Awaited(PipeWriter writer, ValueTask<int> pending, CancellationToken cancellationToken)
            {
                var result = Complete(writer, await pending.ConfigureAwait(false));
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            static async ValueTask<bool> AwaitedFlush(ValueTask<FlushResult> flush, bool result)
            {
                await flush.ConfigureAwait(false);
                return result;
            }
        }
    }
}
