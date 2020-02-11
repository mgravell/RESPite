using Resp.Internal;
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    internal sealed class SocketRespConnection : SimpleRespConnection
    {
        private readonly Socket _socket;
        private SocketAwaitableEventArgs _sendArgs, _reveiveArgs;

        internal SocketRespConnection(Socket socket)
        {
            _socket = socket;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _socket?.Dispose();
        }

        private SocketAwaitableEventArgs SendArgs() => _sendArgs ??= new SocketAwaitableEventArgs();
        private SocketAwaitableEventArgs ReceiveArgs() => _reveiveArgs ??= new SocketAwaitableEventArgs();

        protected override int Read(Memory<byte> buffer)
            => /* MemoryMarshal.TryGetArray<byte>(buffer, out var segment)
                ? _socket.Receive(segment.Array, segment.Offset, segment.Count, SocketFlags.None)
                : */ _socket.Receive(buffer.Span);

        protected override void Write(ReadOnlyMemory<byte> buffer)
        {
            /* if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
                _socket.Send(segment.Array, segment.Offset, segment.Count, SocketFlags.None);
            else */
                _socket.Send(buffer.Span);
        }

        protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var args = SendArgs();
            /*if (MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
            else
            {*/
                args.SetBuffer(MemoryMarshal.AsMemory(buffer));
            //}
            if (!_socket.SendAsync(args)) args.Complete();
            await args;
        }

        protected override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var args = ReceiveArgs();
            /*if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
            {
                args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
            else
            {*/
                args.SetBuffer(buffer);
            //}
            if (!_socket.ReceiveAsync(args)) args.Complete();
            return await args;
        }


    }
}
