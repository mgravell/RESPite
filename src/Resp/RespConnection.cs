using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp
{
    public abstract class RespConnection : IDisposable
    {
        public static RespConnection Create(Stream stream) => new StreamRespConnection(stream);
        public static RespConnection Create(Socket socket) => new SocketRespConnection(socket);
        public abstract void Send(in RespValue frame);
        public abstract RespValue Receive();
        public abstract ValueTask SendAsync(RespValue frame, CancellationToken cancellationToken = default);
        public abstract ValueTask<RespValue> ReceiveAsync(CancellationToken cancellationToken = default);

        //public async ValueTask<RespFrame> RequestAsync(RespFrame frame, CancellationToken cancellationToken = default)
        //{
        //    await SendAsync(frame, cancellationToken).ConfigureAwait(false);
        //    return await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        //}

        //public RespFrame RequestAsync(RespFrame frame)
        //{
        //    Send(frame);
        //    return Receive();
        //}

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowCanceled() => throw new OperationCanceledException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowEndOfStream() => throw new EndOfStreamException();
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static void ThrowAborted() => throw new InvalidOperationException("operation aborted");

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }
    }
}
