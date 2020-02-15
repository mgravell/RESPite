using Resp.Internal;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    internal abstract class SimpleRespConnection : RespConnection
    {
        protected abstract int Read(Memory<byte> buffer);

        protected abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

        protected abstract void Write(ReadOnlyMemory<byte> buffer);

        protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

        protected virtual ValueTask FlushAsync(CancellationToken cancellationToken) => default;
        protected virtual void Flush() { }

        private SimplePipe _outBuffer, _inBuffer;

        protected IBufferWriter<byte> InputWriter => _inBuffer;

        private protected SimpleRespConnection()
        {
            _outBuffer = new SimplePipe();
            _inBuffer = new SimplePipe();
        }

        private void Dispose<T>(ref T field) where T : class, IDisposable
        {
            var tmp = field;
            field = null;
            tmp?.Dispose();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Dispose(ref _outBuffer);
                Dispose(ref _inBuffer);
            }
        }

        private bool TryReadFrame(out RespValue frame)
        {
            if (RespValue.TryParse(_inBuffer.GetBuffer(), out frame, out var end))
            {
                _inBuffer.ConsumeTo(end);
                return true;
            }
            return false;
        }

        public sealed override void Send(in RespValue frame)
        {
            frame.Write(_outBuffer);
            var buffer = _outBuffer.GetBuffer();
            if (!buffer.IsEmpty)
            {
                Flush(buffer);
                _outBuffer.ConsumeTo(buffer.End);
            }
        }
        public sealed override ValueTask SendAsync(RespValue frame, CancellationToken cancellationToken = default)
        {
            frame.Write(_outBuffer);
            var buffer = _outBuffer.GetBuffer();
            if (!buffer.IsEmpty)
            {
                var pending = FlushAsync(buffer, cancellationToken);
                if (!pending.IsCompletedSuccessfully) return Awaited(pending, _outBuffer, buffer.End);
                _outBuffer.ConsumeTo(buffer.End);
            }
            return default;

            static async ValueTask Awaited(ValueTask flush, SimplePipe outbuffer, SequencePosition consumed)
            {
                await flush.ConfigureAwait(false);
                outbuffer.ConsumeTo(consumed);
            }
        }

        private ValueTask FlushAsync(ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
        {
            if (payload.IsSingleSegment)
            {
                var write = this.WriteAsync(payload.First, cancellationToken);
                if (!write.IsCompletedSuccessfully) return AwaitedWrite(this, write, cancellationToken);
                return this.FlushAsync(cancellationToken);
            }
            else
            {
                return FlushSlowAsync(this, payload, cancellationToken);
            }
            static async ValueTask FlushSlowAsync(SimpleRespConnection connection, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
            {
                foreach (var segment in payload)
                {
                    await connection.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                }
                await connection.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            static async ValueTask AwaitedWrite(SimpleRespConnection connection, ValueTask write, CancellationToken cancellationToken)
            {
                await write.ConfigureAwait(false);
                await connection.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private void Flush(in ReadOnlySequence<byte> payload)
        {
            if (payload.IsSingleSegment)
            {
                this.Write(payload.First);
            }
            else
            {
                foreach (var segment in payload)
                {
                    this.Write(segment);
                }
            }
            this.Flush();
        }

        public sealed override RespValue Receive()
        {
            RespValue frame;
            while (!TryReadFrame(out frame))
            {
                if (!ReadMore()) ThrowEndOfStream();
            }
            return frame;
        }

        public sealed override ValueTask<RespValue> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            RespValue frame;
            while (!TryReadFrame(out frame))
            {
                var read = ReadMoreAsync(cancellationToken);
                if (!read.IsCompletedSuccessfully) return Awaited(this, read, cancellationToken);
                if (!read.Result) ThrowEndOfStream();
            }
            return new ValueTask<RespValue>(frame);

            static async ValueTask<RespValue> Awaited(SimpleRespConnection obj, ValueTask<bool> pending, CancellationToken cancellationToken)
            {

                while (true)
                {
                    if (!await pending.ConfigureAwait(false)) ThrowEndOfStream();
                    if (obj.TryReadFrame(out var frame)) return frame;
                    pending = obj.ReadMoreAsync(cancellationToken);
                }
            }
        }

        const int READ_BUFFER_SIZE = 512;
        private bool ReadMore()
        {
            var writer = InputWriter;
            var memory = writer.GetMemory(READ_BUFFER_SIZE);
            var bytes = Read(memory);
            return Complete(writer, bytes);
        }

        private ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
        {
            var writer = InputWriter;
            var memory = writer.GetMemory(READ_BUFFER_SIZE);
            var pending = ReadAsync(memory, cancellationToken);

            if (!pending.IsCompletedSuccessfully) return Awaited(writer, pending);

            return new ValueTask<bool>(Complete(writer, pending.Result));

            static async ValueTask<bool> Awaited(IBufferWriter<byte> writer, ValueTask<int> pending)
            {
                var result = Complete(writer, await pending.ConfigureAwait(false));
                return result;
            }
        }

        private static bool Complete(IBufferWriter<byte> writer, int bytes)
        {
            if (bytes > 0)
            {
                writer.Advance(bytes);
                return true;
            }
            writer.Advance(0);
            return false;
        }
    }
}
