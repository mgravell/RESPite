using Resp.Internal;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    internal abstract class SimpleRespConnection : RespConnection
    {
        private readonly SimplePipe _outBuffer, _inBuffer;

        protected IBufferWriter<byte> InputWriter => _inBuffer;

        private protected SimpleRespConnection()
        {
            _outBuffer = new SimplePipe();
            _inBuffer = new SimplePipe();
        }

        private bool TryReadFrame(out RawFrame frame)
        {
            if (RawFrame.TryParse(_inBuffer.GetBuffer(), out frame, out var end))
            {
                _inBuffer.ConsumeTo(end);
                return true;
            }
            return false;
        }

        public sealed override void Send(in RawFrame frame)
        {
            frame.Write(_outBuffer);
            var buffer = _outBuffer.GetBuffer();
            if (!buffer.IsEmpty)
            {
                Flush(buffer);
                _outBuffer.ConsumeTo(buffer.End);
            }
        }
        public sealed override ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken = default)
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
