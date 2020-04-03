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

        private int _flags, _pendingCount;

        private void SetFlag(RespConnectionFlags flag, bool value)
        {
            int newValue, oldValue;
            do
            {
                oldValue = Volatile.Read(ref _flags);
                newValue = value ? (oldValue | (int)flag) : (oldValue & ~(int)flag);
            } while (oldValue != newValue && Interlocked.CompareExchange(ref _flags, newValue, oldValue) == oldValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetFlag(RespConnectionFlags flag)
        {
            var flags = (RespConnectionFlags)Volatile.Read(ref _flags);
            return (flags & flag) != 0;
        }

        /// <summary>
        /// Gets the number of outstanding responses we can expect, based on request/response being equal
        /// </summary>
        public int OutstandingResponseCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _pendingCount);
        }

        public void Doom() => SetFlag(RespConnectionFlags.IsDoomed, true);
        internal bool IsReusable => OutstandingResponseCount == 0 & !GetFlag(RespConnectionFlags.IsDoomed);

        public static RespConnection Create(Stream stream) => new StreamRespConnection(stream);
        public static RespConnection Create(Socket socket) => Create(new NetworkStream(socket));
            // => new SocketRespConnection(socket);
        public void Send(in RespValue value)
        {
            Interlocked.Increment(ref _pendingCount);
            try
            {
                OnSend(in value);
            }
            catch
            {
                Doom();
                throw;
            }
        }
        public Lifetime<RespValue> Receive()
        {
            try
            {
                var result = OnReceive();
                Interlocked.Decrement(ref _pendingCount);
                return result;
            }
            catch
            {
                Doom();
                throw;
            }
        }

        protected abstract void OnSend(in RespValue value);
        protected abstract Lifetime<RespValue> OnReceive();

        public virtual T Call<T>(in RespValue command, Func<RespValue, T> selector)
        {
            Send(command);
            using var response = Receive();
            return selector(response.Value);
        }
        public virtual void Call(in RespValue command, Action<RespValue> validator)
        {
            Send(command);
            using var response = Receive();
            validator(response.Value);
        }

        public ValueTask SendAsync(RespValue value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _pendingCount);
            var pending = OnSendAsync(value, cancellationToken);
            return pending.IsCompletedSuccessfully ? default : Awaited(this, pending);

            async static ValueTask Awaited(RespConnection @this, ValueTask pending)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch
                {
                    @this.Doom();
                    throw;
                }
            }
        }

        public ValueTask<Lifetime<RespValue>> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            var pending = OnReceiveAsync(cancellationToken);
            if (pending.IsCompletedSuccessfully)
            {
                Interlocked.Decrement(ref _pendingCount);
                return new ValueTask<Lifetime<RespValue>>(pending.Result);
            }
            return Awaited(this, pending);

            async static ValueTask<Lifetime<RespValue>> Awaited(RespConnection @this, ValueTask<Lifetime<RespValue>> pending)
            {
                try
                {
                    var result = await pending.ConfigureAwait(false);
                    Interlocked.Decrement(ref @this._pendingCount);
                    return result;
                }
                catch
                {
                    @this.Doom();
                    throw;
                }
            }
        }

        protected abstract ValueTask OnSendAsync(RespValue value, CancellationToken cancellationToken);

        protected abstract ValueTask<Lifetime<RespValue>> OnReceiveAsync(CancellationToken cancellationToken);

        public virtual async ValueTask<T> CallAsync<T>(RespValue command, Func<RespValue, T> selector, CancellationToken cancellationToken = default)
        {
            await SendAsync(command, cancellationToken).ConfigureAwait(false);
            using var response = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return selector(response.Value);
        }

        public virtual async ValueTask CallAsync(RespValue command, Action<RespValue> validator, CancellationToken cancellationToken = default)
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
