using Bedrock.Framework.Protocols;
using Microsoft.AspNetCore.Connections;
using Resp;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BedrockRespProtocol
{
    public abstract class RespClientProtocol
    {
        public abstract void Send(in RawFrame frame);
        public abstract RawFrame Receive();
        public abstract ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken = default);
        public abstract ValueTask<RawFrame> ReceiveAsync(CancellationToken cancellationToken = default);

        public TimeSpan PingRaw()
        {
            var before = DateTime.UtcNow;
            Send(RawFrame.Ping);
            var pong = Receive();
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }
        public async ValueTask<TimeSpan> PingRawAsync(CancellationToken cancellationToken = default)
        {
            var before = DateTime.UtcNow;
            await SendAsync(RawFrame.Ping, cancellationToken).ConfigureAwait(false);
            var pong = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var after = DateTime.UtcNow;
            if (!pong.IsShortAlphaIgnoreCase(Pong)) Wat();
            return after - before;
        }

        static void Wat() => throw new InvalidOperationException("something went terribly wrong");

        private static readonly ulong Pong = RawFrame.EncodeShortASCII("pong");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected static void ThrowCanceled() => throw new OperationCanceledException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected static void ThrowEndOfStream() => throw new EndOfStreamException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected static void ThrowAborted() => throw new ConnectionAbortedException();

        //public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        //{
        //    var before = DateTime.UtcNow;
        //    await SendAsync(RedisFrame.Ping, cancellationToken).ConfigureAwait(false);
        //    using var pong = await ReadAsync(cancellationToken).ConfigureAwait(false);
        //    var after = DateTime.UtcNow;
        //    if (!(pong is RedisSimpleString rss && rss.Equals("PONG", StringComparison.OrdinalIgnoreCase))) Wat();
        //    return after - before;
        //}
    }

    public abstract class RespPipeBufferedProtocol : RespClientProtocol
    {
        private readonly Pipe _sendBuffer, _receiveBuffer;

        protected PipeWriter InputWriter => _receiveBuffer.Writer;

        private protected RespPipeBufferedProtocol()
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

            static async ValueTask AwaitedFlush(ValueTask<FlushResult> flush, RespPipeBufferedProtocol obj, CancellationToken cancellationToken)
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

            static async ValueTask<RawFrame> Awaited(RespPipeBufferedProtocol obj, ValueTask<bool> pending, CancellationToken cancellationToken)
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
    public sealed class RespStreamProtocol : RespPipeBufferedProtocol
    {
        private readonly Stream _stream;

        public RespStreamProtocol(Socket socket) : this(new NetworkStream(socket, true)) { }
        public RespStreamProtocol(Stream stream)
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
                foreach(var segment in payload)
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
                foreach(var segment in payload)
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

    public sealed class RespBedrockProtocol : RespClientProtocol
    {
        // private readonly ConnectionContext _connection;
        private readonly ProtocolReader _reader;
        private readonly ProtocolWriter _writer;

        public RespBedrockProtocol(ConnectionContext connection)
        {
            // _connection = connection;
            _reader = connection.CreateReader();
            _writer = connection.CreateWriter();
        }

        //public ValueTask<RedisFrame> ReadAsync(CancellationToken cancellationToken = default)
        //    => ReadAsync<RedisFrame>(_reader, Resp2ClientReader.Instance, cancellationToken);

        //public ValueTask SendAsync(RedisFrame frame, CancellationToken cancellationToken = default)
        //    => _writer.WriteAsync<RedisFrame>(Resp2ClientWriter.Instance, frame, cancellationToken);

        public override void Send(in RawFrame frame)
        {
            var vt = SendAsync(frame, default);
            if (!vt.IsCompletedSuccessfully) vt.AsTask().Wait();
        }

        public override RawFrame Receive()
        {
            var vt = ReceiveAsync(default);
            return vt.IsCompletedSuccessfully ? vt.Result : vt.AsTask().Result;
        }


        public override ValueTask SendAsync(RawFrame frame, CancellationToken cancellationToken)
            => _writer.WriteAsync<RawFrame>(Resp2ClientWriter.Instance, frame, cancellationToken);

        public override ValueTask<RawFrame> ReceiveAsync(CancellationToken cancellationToken)
            => ReadAsync<RawFrame>(_reader, Resp2ClientReader.Instance, cancellationToken);

        private static ValueTask<T> ReadAsync<T>(ProtocolReader source, IMessageReader<T> parser, CancellationToken cancellationToken)
        {
            var result = source.ReadAsync<T>(parser, cancellationToken);
            // avoid the async machinery if we already have the result on the pipe
            return result.IsCompletedSuccessfully ? new ValueTask<T>(Validate(source, result.Result)) : Awaited(source, result);

            static async ValueTask<T> Awaited(ProtocolReader reader, ValueTask<ProtocolReadResult<T>> result)
                => Validate(reader, await result.ConfigureAwait(false));

            static T Validate(ProtocolReader reader, in ProtocolReadResult<T> result)
            {
                reader.Advance();
                if (result.IsCanceled) ThrowCanceled();
                if (result.IsCompleted) ThrowAborted();
                return result.Message;
            }
        }
    }
}
