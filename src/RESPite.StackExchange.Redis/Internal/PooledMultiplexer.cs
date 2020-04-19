using Pipelines.Sockets.Unofficial;
using Respite;
using StackExchange.Redis;
using StackExchange.Redis.Profiling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.StackExchange.Redis.Internal
{
    internal sealed class PooledMultiplexer : IConnectionMultiplexer, IAsyncDisposable
    {
        private readonly RespConnectionPool _pool;
        private int _nextConnectionIndex;
        public PooledMultiplexer(ConfigurationOptions configuration, RespConnectionPoolOptions options)
        {
            Configuration = configuration.Clone();
            if (configuration.EndPoints.Count != 1) throw new ArgumentException("A single endpoint is expected", nameof(configuration));
            _pool = new RespConnectionPool(cancellation => ConnectAsync(cancellation), options);
        }

        public ValueTask DisposeAsync() => _pool.DisposeAsync();

        internal ValueTask<AsyncLifetime<RespConnection>> RentAsync(CancellationToken cancellationToken)
            => _pool.RentAsync(cancellationToken);

        internal Lifetime<RespConnection> Rent()
            => _pool.Rent();

        private ValueTask<RespConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _nextConnectionIndex);
            var name = $"connection " + index;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Connect(Configuration.EndPoints.Single());
            return HandshakeAsync(RespConnection.Create(new NetworkStream(socket), name), cancellationToken);
        }

        private ValueTask<RespConnection> HandshakeAsync(RespConnection connection, CancellationToken cancellationToken)
            => new ValueTask<RespConnection>(connection);

        long _opCount;

        internal void IncrementOpCount() => Interlocked.Increment(ref _opCount);
        internal async Task CallAsync(Lifetime<Memory<RespValue>> args, CancellationToken cancellationToken, Action<RespValue>? inspector = null)
        {
            using (args)
            {
                Interlocked.Increment(ref _opCount);
                await using var lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
                await lease.Value.SendAsync(RespValue.CreateAggregate(RespType.Array, args.Value), cancellationToken).ConfigureAwait(false);
                using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                response.Value.ThrowIfError();
                inspector?.Invoke(response.Value);
            }
        }
        internal void Call(RespConnection connection, Lifetime<Memory<RespValue>> args, Action<RespValue>? inspector = null)
        {
            using (args)
            {
                Interlocked.Increment(ref _opCount);
                connection.Send(RespValue.CreateAggregate(RespType.Array, args.Value));
            }
            using var response = connection.Receive();
            response.Value.ThrowIfError();
            inspector?.Invoke(response.Value);
        }

        internal async Task<T> CallAsync<T>(Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector, CancellationToken cancellationToken)
        {
            using (args)
            {
                Interlocked.Increment(ref _opCount);
                await using var lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
                await lease.Value.SendAsync(RespValue.CreateAggregate(RespType.Array, args.Value), cancellationToken).ConfigureAwait(false);
                using var response = await lease.Value.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                response.Value.ThrowIfError();
                return selector(response.Value);
            }
        }

        internal T Call<T>(RespConnection connection, Lifetime<Memory<RespValue>> args, Func<RespValue, T> selector)
        {
            using (args)
            {
                Interlocked.Increment(ref _opCount);
                connection.Send(RespValue.CreateAggregate(RespType.Array, args.Value));
            }
            using var response = connection.Receive();
            response.Value.ThrowIfError();
            return selector(response.Value);
        }

        internal async Task CallAsync(RespConnection connection, List<IBatchedOperation> operations, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var op in operations) // send all
                {
                    Interlocked.Increment(ref _opCount);
                    using var args = op.ConsumeArgs();
                    await connection.SendAsync(RespValue.CreateAggregate(RespType.Array, args.Value), cancellationToken).ConfigureAwait(false);
                }
                foreach (var op in operations) // then receive all
                {
                    using var response = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        response.Value.ThrowIfError();
                        op.ProcessResponse(response.Value);
                    }
                    catch (Exception ex)
                    {
                        op.TrySetException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var op in operations) // fault anything that is left after a global explosion
                {
                    op.TrySetException(ex);
                }
            }
        }



        internal void Call(RespConnection connection, List<IBatchedOperation> operations)
        {
            static void Send(PooledMultiplexer @this, RespConnection connection, IBatchedOperation op, bool flush)
            {
                Interlocked.Increment(ref @this._opCount);
                using var args = op.ConsumeArgs();
                connection.Send(RespValue.CreateAggregate(RespType.Array, args.Value), flush);
            }
            static void BeginSendInBackground(PooledMultiplexer @this, RespConnection connection,
                List<IBatchedOperation> values)
                => Task.Run(() =>
                {
                    var len = values.Count;
                    try
                    {
                        for (int i = 1; i < len; i++)
                        {
                            Send(@this, connection, values[i], flush: i == len - 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        connection.Doom();
                        Console.WriteLine("EEK!");
                        Console.WriteLine(ex.Message);
                    }
                });
            try
            {
                int len = operations.Count;
                if (len == 0) return;
                Send(this, connection, operations[0], true);
                if (len != 1) BeginSendInBackground(this, connection, operations);

                foreach (var op in operations) // then receive all
                {
                    using var response = connection.Receive();
                    try
                    {
                        response.Value.ThrowIfError();
                        op.ProcessResponse(response.Value);
                    }
                    catch (Exception ex)
                    {
                        op.TrySetException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EEK!");
                Console.WriteLine(ex.Message);
                connection.Doom();
                foreach (var op in operations) // fault anything that is left after a global explosion
                {
                    op.TrySetException(ex);
                }
            }
        }

        public IDatabaseAsync GetDatabaseAsync(int db = -1, in CancellationToken cancellationToken = default)
            => new PooledDatabase(this, db, cancellationToken);

        IDatabase IConnectionMultiplexer.GetDatabase(int db, object? asyncState)
            => new PooledDatabase(this, db, default);

        internal ConfigurationOptions Configuration { get; }
        string IConnectionMultiplexer.Configuration => Configuration.ToString();

        string IConnectionMultiplexer.ClientName => Configuration.ClientName;

        int IConnectionMultiplexer.TimeoutMilliseconds => TimeoutMilliseconds;
        internal int TimeoutMilliseconds => Configuration.SyncTimeout;

        long IConnectionMultiplexer.OperationCount => Volatile.Read(ref _opCount);

        [Obsolete]
        bool IConnectionMultiplexer.PreserveAsyncOrder {
            get => false;
            set { }
        }

        bool IConnectionMultiplexer.IsConnected => true;

        bool IConnectionMultiplexer.IsConnecting => false;

        bool IConnectionMultiplexer.IncludeDetailInExceptions
        {
            get => false;
            set { }
        }
        int IConnectionMultiplexer.StormLogThreshold { get => 0; set { } }

        event EventHandler<RedisErrorEventArgs>? IConnectionMultiplexer.ErrorMessage { add { } remove { } }
        event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionFailed { add { } remove { } }
        event EventHandler<InternalErrorEventArgs>? IConnectionMultiplexer.InternalError { add { } remove { } }
        event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionRestored { add { } remove { } }
        event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChanged { add { } remove { } }
        event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChangedBroadcast { add { } remove { } }
        event EventHandler<HashSlotMovedEventArgs>? IConnectionMultiplexer.HashSlotMoved { add { } remove { } }

        void IConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession> profilingSessionProvider) { }

        ServerCounters IConnectionMultiplexer.GetCounters() => throw new NotImplementedException();

        EndPoint[] IConnectionMultiplexer.GetEndPoints(bool configuredOnly) => Configuration.EndPoints.ToArray();

        void IConnectionMultiplexer.Wait(Task task) => Wait(task);
        internal void Wait(Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            try
            {
                if (!task.Wait(TimeoutMilliseconds)) throw new TimeoutException();
            }
            catch (AggregateException aex) when (IsSingle(aex))
            {
                throw aex.InnerExceptions[0];
            }
        }
        private static bool IsSingle(AggregateException aex)
        {
            try { return aex != null && aex.InnerExceptions.Count == 1; }
            catch { return false; }
        }

        T IConnectionMultiplexer.Wait<T>(Task<T> task) => Wait(task);
        internal T Wait<T>(Task<T> task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            try
            {
                if (!task.Wait(TimeoutMilliseconds)) throw new TimeoutException();
            }
            catch (AggregateException aex) when (IsSingle(aex))
            {
                throw aex.InnerExceptions[0];
            }
            return task.Result;
        }

        void IConnectionMultiplexer.WaitAll(params Task[] tasks)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0) return;
            if (!Task.WaitAll(tasks, TimeoutMilliseconds)) throw new TimeoutException();
        }

        int IConnectionMultiplexer.HashSlot(RedisKey key) => throw new NotImplementedException();

        ISubscriber IConnectionMultiplexer.GetSubscriber(object? asyncState) => throw new NotImplementedException();

        IServer IConnectionMultiplexer.GetServer(string host, int port, object? asyncState) => throw new NotImplementedException();

        IServer IConnectionMultiplexer.GetServer(string hostAndPort, object? asyncState) => throw new NotImplementedException();

        IServer IConnectionMultiplexer.GetServer(IPAddress host, int port) => throw new NotImplementedException();

        IServer IConnectionMultiplexer.GetServer(EndPoint endpoint, object? asyncState) => throw new NotImplementedException();

        Task<bool> IConnectionMultiplexer.ConfigureAsync(TextWriter? log) => throw new NotImplementedException();

        bool IConnectionMultiplexer.Configure(TextWriter? log) => throw new NotImplementedException();

        string IConnectionMultiplexer.GetStatus() => "";

        void IConnectionMultiplexer.GetStatus(TextWriter log) { }

        void IConnectionMultiplexer.Close(bool allowCommandsToComplete) => ((IDisposable)this).Dispose();

        Task IConnectionMultiplexer.CloseAsync(bool allowCommandsToComplete) => DisposeAsync().AsTask();

        string IConnectionMultiplexer.GetStormLog() => "";
        void IConnectionMultiplexer.ResetStormLog() { }

        long IConnectionMultiplexer.PublishReconfigure(CommandFlags flags) => throw new NotImplementedException();
    
        Task<long> IConnectionMultiplexer.PublishReconfigureAsync(CommandFlags flags) => throw new NotImplementedException();

        int IConnectionMultiplexer.GetHashSlot(RedisKey key) => throw new NotImplementedException();

        void IConnectionMultiplexer.ExportConfiguration(Stream destination, ExportOptions options) => throw new NotImplementedException();

        void IDisposable.Dispose() => DisposeAsync().AsTask().Wait(TimeoutMilliseconds);
    }
}
