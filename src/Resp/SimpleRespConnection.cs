using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    internal abstract class SimpleRespConnection : RespConnection
    {
        private readonly Pipe _sendBuffer, _receiveBuffer;

        protected PipeWriter InputWriter => _receiveBuffer.Writer;

        private protected SimpleRespConnection()
        {
            _sendBuffer = new Pipe();
            _receiveBuffer = new Pipe();
        }

        private bool TryReadFrame(out RawFrame frame)
        {
            var reader = _receiveBuffer.Reader;
            if (reader.TryRead(out var result))
            {
                if (result.IsCanceled) ThrowCanceled();
                if (result.IsCompleted) ThrowEndOfStream();

                var buffer = result.Buffer;
                if (RawFrame.TryParse(buffer, out frame, out var end))
                {
                    reader.AdvanceTo(end, end);
                    return true;
                }
                else
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            frame = default;
            return false;
        }

        private protected static void FlushSync(PipeWriter writer)
        {
            var flush = writer.FlushAsync();
            if (!flush.IsCompletedSuccessfully) flush.AsTask().Wait();
        }
        public sealed override void Send(in RawFrame frame)
        {
            frame.Write(_sendBuffer.Writer);
            FlushSync(_sendBuffer.Writer);
            if (_sendBuffer.Reader.TryRead(out var result))
            {
                var buffer = result.Buffer;
                if (!buffer.IsEmpty)
                {
                    Flush(buffer);
                }
                _sendBuffer.Reader.AdvanceTo(buffer.End);
            }
        }
        public sealed override ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken = default)
        {
            frame.Write(_sendBuffer.Writer);
            var flush = _sendBuffer.Writer.FlushAsync(cancellationToken);
            if (!flush.IsCompletedSuccessfully) return AwaitedFlush(flush, this, cancellationToken);

            if (_sendBuffer.Reader.TryRead(out var result))
            {
                var buffer = result.Buffer;
                if (!buffer.IsEmpty)
                {
                    var pending = FlushAsync(buffer, cancellationToken);
                    if (!pending.IsCompletedSuccessfully) return Awaited(pending, _sendBuffer.Reader, buffer.End);
                }
                _sendBuffer.Reader.AdvanceTo(buffer.End);
            }
            return default;

            static async ValueTask AwaitedFlush(ValueTask<FlushResult> flush, SimpleRespConnection obj, CancellationToken cancellationToken)
            {
                await flush.ConfigureAwait(false);
                if (obj._sendBuffer.Reader.TryRead(out var result))
                {
                    var buffer = result.Buffer;
                    if (!buffer.IsEmpty)
                    {
                        await obj.FlushAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                    obj._sendBuffer.Reader.AdvanceTo(buffer.End);
                }

            }
            static async ValueTask Awaited(ValueTask flush, PipeReader reader, SequencePosition consumed)
            {
                await flush.ConfigureAwait(false);
                reader.AdvanceTo(consumed);
            }
        }

        protected abstract void Flush(in ReadOnlySequence<byte> payload);
        protected abstract ValueTask FlushAsync(ReadOnlySequence<byte> payload, CancellationToken cancellationToken);

        public override RawFrame Receive()
        {
            RawFrame frame;
            while (!TryReadFrame(out frame))
            {
                if (!ReadMore()) ThrowEndOfStream();
            }
            return frame;
        }

        public sealed override ValueTask<RawFrame> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            RawFrame frame;
            while (!TryReadFrame(out frame))
            {
                var read = ReadMoreAsync(cancellationToken);
                if (!read.IsCompletedSuccessfully) return Awaited(this, read, cancellationToken);
                if (!read.Result) ThrowEndOfStream();
            }
            return new ValueTask<RawFrame>(frame);

            static async ValueTask<RawFrame> Awaited(SimpleRespConnection obj, ValueTask<bool> pending, CancellationToken cancellationToken)
            {

                while (true)
                {
                    if (!await pending.ConfigureAwait(false)) ThrowEndOfStream();
                    if (obj.TryReadFrame(out var frame)) return frame;
                    pending = obj.ReadMoreAsync(cancellationToken);
                }
            }
        }

        protected abstract ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken);
        protected abstract bool ReadMore();
    }
}
