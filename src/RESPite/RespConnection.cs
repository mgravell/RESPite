using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    public abstract partial class RespConnection : IDisposable
    {
        public static RespConnection Create(Stream stream) => new StreamRespConnection(stream);
        public static RespConnection Create(Socket socket) => new SocketRespConnection(socket);
        public abstract void Send(in RespValue value);
        public abstract Lifetime<RespValue> Receive();

        public T Call<T>(in RespValue command, Func<RespValue, T> selector)
        {
            Send(command);
            using var response = Receive();
            return selector(response.Value);
        }
        public void Call(in RespValue command, Action<RespValue> validator)
        {
            Send(command);
            using var response = Receive();
            validator(response.Value);
        }

        public abstract ValueTask SendAsync(RespValue value, CancellationToken cancellationToken = default);

        public abstract ValueTask<Lifetime<RespValue>> ReceiveAsync(CancellationToken cancellationToken = default);

        public async ValueTask<T> CallAsync<T>(RespValue command, Func<RespValue, T> selector, CancellationToken cancellationToken = default)
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
            using var response = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return selector(response.Value);
        }

        public async ValueTask CallAsync(RespValue command, Action<RespValue> validator, CancellationToken cancellationToken = default)
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
            using var response = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            validator(response.Value);
        }

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
