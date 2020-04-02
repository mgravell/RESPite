using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Respite
{
    public abstract partial class RespConnection : IAsyncDisposable
    {
        [Flags]
        enum RespConnectionFlags
        {
            None = 0,
            IsDoomed = 1 << 0,
        }

        private int _flags;
        private void SetFlag(RespConnectionFlags flag, bool value)
        {
            int newValue, oldValue;
            do
            {
                oldValue = Volatile.Read(ref _flags);
                newValue = value ? (oldValue | (int)flag) : (oldValue & ~(int)flag);
            } while (oldValue != newValue && Interlocked.CompareExchange(ref _flags, newValue, oldValue) == oldValue);
        }
        private bool GetFlag(RespConnectionFlags flag)
        {
            var flags = (RespConnectionFlags)Volatile.Read(ref _flags);
            return (flags & flag) != 0;
        }
        public void Doom() => SetFlag(RespConnectionFlags.IsDoomed, true);
        public bool IsDoomed => GetFlag(RespConnectionFlags.IsDoomed);

        public static RespConnection Create(Stream stream) => new StreamRespConnection(stream);
        public static RespConnection Create(Socket socket) => Create(new NetworkStream(socket));
            // => new SocketRespConnection(socket);
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

        public ValueTask DisposeAsync()
        {
            Doom();
            GC.SuppressFinalize(this);
            return OnDisposeAsync();
        }

        protected virtual ValueTask OnDisposeAsync() => default;
    }
}
