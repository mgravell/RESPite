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

        SequencePosition _currentEnd;
        bool _haveActive;
        private bool TryRead(out Lifetime<RespValue> value)
        {
            if (_haveActive) ThrowHelper.Invalid("Existing result must be discarded");
            _haveActive = true;
            if (RespValue.TryParse(_inBuffer.GetBuffer(), out var raw, out var end, out var bytes))
            {
                TotalBytesRead += bytes;
                _currentEnd = end;
                value = new Lifetime<RespValue>(raw, (_, state) => ((SimpleRespConnection)state).Advance(), this);
                return true;
            }
            _haveActive = false;
            value = default;
            return false;
        }

        private void Advance()
        {
            var end = _currentEnd;
            _currentEnd = default;
            _inBuffer.ConsumeTo(end);
            _haveActive = false;
        }

        public RespVersion Version { get; set; } = RespVersion.RESP2;

        public long TotalBytesSent { get; private set; }
        public long TotalBytesRead { get; private set; }

        public sealed override void Send(in RespValue value)
        {
            TotalBytesSent += value.Write(_outBuffer, Version);
            var buffer = _outBuffer.GetBuffer();
            if (!buffer.IsEmpty)
            {
                Flush(buffer);
                _outBuffer.ConsumeTo(buffer.End);
            }
        }
        public sealed override ValueTask SendAsync(RespValue value, CancellationToken cancellationToken = default)
        {
            value.Write(_outBuffer, Version);
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

        public sealed override Lifetime<RespValue> Receive()
        {
            Lifetime<RespValue> value;
            while (!TryRead(out value))
            {
                if (!ReadMore()) ThrowEndOfStream();
            }
            return value;
        }

        public sealed override ValueTask<Lifetime<RespValue>> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            Lifetime<RespValue> value;
            while (!TryRead(out value))
            {
                var read = ReadMoreAsync(cancellationToken);
                if (!read.IsCompletedSuccessfully) return Awaited(this, read, cancellationToken);
                if (!read.Result) ThrowEndOfStream();
            }
            return new ValueTask<Lifetime<RespValue>>(value);

            static async ValueTask<Lifetime<RespValue>> Awaited(SimpleRespConnection obj, ValueTask<bool> pending, CancellationToken cancellationToken)
            {

                while (true)
                {
                    if (!await pending.ConfigureAwait(false)) ThrowEndOfStream();
                    if (obj.TryRead(out var value)) return value;
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
