using Bedrock.Framework;
using Microsoft.Extensions.DependencyInjection;
using Pipelines.Sockets.Unofficial;
using Respite;
using Respite.Bedrock;
using Respite.Redis;
using StackExchange.Redis;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleClient
{
    class Program
    {
        private static readonly EndPoint ServerEndpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        static async Task Main()
        {
            using var redis = await RedisConnection.ConnectAsync(ServerEndpoint);



            await redis.CallAsync("del", "foo");
            await redis.CallAsync("lpush", "foo", 123);
            await redis.CallAsync("lpush", "foo", 456);
            Console.WriteLine(await redis.CallAsync("llen", "foo"));
            var arr = (object[]) await redis.CallAsync("lrange", "foo", 0, -1);
            foreach (var obj in arr)
                Console.WriteLine(obj);

        }

#pragma warning disable IDE0051 // Remove unused private members
        static void Main2()
#pragma warning restore IDE0051 // Remove unused private members
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Connect(ServerEndpoint);
            using var client = RespConnection.Create(socket);
            var payload = new string('a', 2048);
            var frame = RespValue.CreateAggregate(RespType.Array, "ping", payload);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                client.Send(frame);
                using var reply = client.Receive();
                reply.Value.ThrowIfError();
                // client.Ping();
            }
            timer.Stop();
            Log("sync", timer.Elapsed, 1000, payload);
        }
#pragma warning disable IDE0051 // Remove unused private members
        static async Task Main3()
#pragma warning restore IDE0051 // Remove unused private members
        {
            const int CLIENTS = 20, PER_CLIENT = 10000, PIPELINE_DEPTH = 20;

            string payload = null; // "abc"; //  new string('a', 2048);
            for (int i = 0; i < 3; i++)
            {
                await ExecuteSocketAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                await ExecuteNetworkStreamAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                await ExecuteStackExchangeRedisAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                await ExecuteBedrockAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
            }
        }

        static RespConnection[] CreateClients(int count, bool asNetworkStream)
        {
            var clients = new RespConnection[count];

            for (int i = 0; i < clients.Length; i++)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                SocketConnection.SetRecommendedClientOptions(socket);
                socket.Connect(ServerEndpoint);
                var connection = asNetworkStream ? RespConnection.Create(new NetworkStream(socket)) : RespConnection.Create(socket);
                // connection.Ping();
                
                clients[i] = connection;
            }

            return clients;

        }


        static readonly RespValue
            s_ping = RespValue.CreateAggregate(RespType.Array, "PING"),
            s_pong = RespValue.Create(RespType.SimpleString, "PONG");

        static async Task RunClientAsync(RespConnection client, int pingsPerClient, int pipelineDepth, string payload)
        {
            var frame = string.IsNullOrEmpty(payload)
                ? s_ping
                : RespValue.CreateAggregate(RespType.Array, "PING", payload);
            var expected = string.IsNullOrEmpty(payload)
                ? s_pong
                : RespValue.Create(RespType.BlobString, payload);

            if (pipelineDepth == 1)
            {
                for (int i = 0; i < pingsPerClient; i++)
                {
                    await client.SendAsync(frame).ConfigureAwait(false);
                    using var result = await client.ReceiveAsync().ConfigureAwait(false);
                    result.Value.ThrowIfError();

                    if (!result.Value.Equals(expected)) Throw();
                    // await client.PingAsync();
                }
            }
            else
            {
                using var frames = Replicate(frame, pipelineDepth);
                for (int i = 0; i < pingsPerClient; i++)
                {
                    using var batch = await client.BatchAsync(frames.Value).ConfigureAwait(false);
                    CheckBatchForErrors(batch.Value, expected);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw() => throw new InvalidOperationException();

        static async Task RunClientAsync(IDatabase client, int pingsPerClient, int pipelineDepth, object[] args)
        {
            for (int i = 0; i < pingsPerClient; i++)
            {
                if (pipelineDepth == 1)
                {
                    await client.ExecuteAsync("ping", args).ConfigureAwait(false);
                }
                else
                {
                    var batch = client.CreateBatch();
                    using var results = BatchPing(batch, pipelineDepth, args);
                    batch.Execute();
                    for (int j = 0; j < pipelineDepth; j++)
                        await results.Value.Span[j].ConfigureAwait(false);
                }
            }
        }

        static Lifetime<Memory<RespValue>> Replicate(in RespValue frame, int count)
        {
            var lease = RespValue.Lease(count);
            lease.Value.Span.Fill(frame);
            return lease;
        }

        static void RunClient(RespConnection client, int pingsPerClient, int pipelineDepth, string payload)
        {
            var frame = string.IsNullOrEmpty(payload)
                ? s_ping
                : RespValue.CreateAggregate(RespType.Array, "ping", payload);
            var expected = string.IsNullOrEmpty(payload)
                ? s_pong
                : RespValue.Create(RespType.BlobString, payload);
            if (pipelineDepth == 1)
            {
                for (int i = 0; i < pingsPerClient; i++)
                {
                    client.Send(frame);
                    using var result = client.Receive();
                    result.Value.ThrowIfError();

                    if (!result.Value.Equals(expected)) Throw();
                    // client.Ping();
                }
            }
            else
            {
                using var frames = Replicate(frame, pipelineDepth);
                for (int i = 0; i < pingsPerClient; i++)
                {
                    using var batch = client.Batch(frames.Value);
                    CheckBatchForErrors(batch.Value, expected);
                }
            }
        }

        static void CheckBatchForErrors(ReadOnlyMemory<RespValue> values, in RespValue expected)
        {
            foreach (var value in values.Span)
            {
                value.ThrowIfError();
                if (!value.Equals(expected)) Throw();
            }
        }
        static void RunClient(IDatabase client, int pingsPerClient, int pipelineDepth, object[] args)
        {
            for (int i = 0; i < pingsPerClient; i++)
            {
                if (pipelineDepth == 1)
                {
                    client.Execute("ping", args);
                }
                else
                {
                    var batch = client.CreateBatch();
                    using var results = BatchPing(batch, pipelineDepth, args);
                    batch.Execute();
                    foreach (var result in results.Value.Span)
                        result.Wait();
                }
            }
        }

        static Lifetime<ReadOnlyMemory<Task>> BatchPing(IBatch batch, int count, object[] args)
        {
            var arr = ArrayPool<Task>.Shared.Rent(count);
            for (int i = 0; i < count; i++)
                arr[i] = batch.ExecuteAsync("ping", args);

            var memory = new ReadOnlyMemory<Task>(arr, 0, count);
            return new Lifetime<ReadOnlyMemory<Task>>(memory, (_, state) => ArrayPool<Task>.Shared.Return((Task[])state), arr);
        }

        static void Log(string label, TimeSpan elapsed, long count, string payload)
        {
            var MiB = (count * 2 * Encoding.UTF8.GetByteCount(payload ?? "")) / (double)(1024 * 1024);
            Console.WriteLine($"{label}: {(int)elapsed.TotalMilliseconds}ms, {count / elapsed.TotalSeconds:###,##0} ops/s, {MiB / elapsed.TotalSeconds:###,##0.00} MiB/s");
        }

        static ValueTask ExecuteSocketAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload)
            => ExecuteDirectAsync(pingsPerClient, clientCount, pipelineDepth, payload, false);

        static ValueTask ExecuteNetworkStreamAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload)
            => ExecuteDirectAsync(pingsPerClient, clientCount, pipelineDepth, payload, true);

        static async ValueTask ExecuteDirectAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload, bool asNetworkStream, [CallerMemberName]string caller = null)
        {
            pingsPerClient /= pipelineDepth;
            var totalPings = pingsPerClient * clientCount * pipelineDepth;
            Console.WriteLine();
            Console.WriteLine(caller);
            Console.WriteLine($"{clientCount} clients, {pingsPerClient}x{pipelineDepth} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(payload ?? "")} bytes");

            var clients = CreateClients(clientCount, asNetworkStream);
            await RunClientAsync(clients[0], 1, pipelineDepth, payload);
            var tasks = new Task[clientCount];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                var client = clients[i];
                tasks[i] = Task.Run(() => RunClientAsync(client, pingsPerClient, pipelineDepth, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);
            
            var threads = new Thread[clientCount];
#pragma warning disable IDE0039 // Use local function
            ParameterizedThreadStart starter = state => RunClient((RespConnection)state, pingsPerClient, pipelineDepth, payload);
#pragma warning restore IDE0039 // Use local function
            timer = Stopwatch.StartNew();
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(starter);
                threads[i].Start(clients[i]);
            }
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }
            timer.Stop();
            Log("sync", timer.Elapsed, totalPings, payload);
        }

        static async Task ExecuteBedrockAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload)
        {
            pingsPerClient /= pipelineDepth;
            var totalPings = pingsPerClient * clientCount * pipelineDepth;
            Console.WriteLine();
            Console.WriteLine(Me());
            Console.WriteLine($"{clientCount} clients, {pingsPerClient}x{pipelineDepth} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(payload ?? "")} bytes");
            var clients = new RespBedrockProtocol[clientCount];
            for (int i = 0; i < clients.Length; i++)
            {
                var serviceProvider = new ServiceCollection().BuildServiceProvider();
                var client = new ClientBuilder(serviceProvider)
                    .UseSockets()
                    .Build();

                var connection = await client.ConnectAsync(ServerEndpoint);

                clients[i] = new RespBedrockProtocol(connection);
            }
            var tasks = new Task[clientCount];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                var client = clients[i];
                tasks[i] = Task.Run(() => RunClientAsync(client, pingsPerClient, pipelineDepth, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);

            var threads = new Thread[clientCount];
#pragma warning disable IDE0039 // Use local function
            ParameterizedThreadStart starter = state => RunClient((RespConnection)state, pingsPerClient, pipelineDepth, payload);
#pragma warning restore IDE0039 // Use local function
            timer = Stopwatch.StartNew();
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(starter);
                threads[i].Start(clients[i]);
            }
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }
            timer.Stop();
            Log("sync", timer.Elapsed, totalPings, payload);
        }

        static async Task ExecuteStackExchangeRedisAsync(int pingsPerWorker, int workers, int pipelineDepth, string sPayload)
        {
            pingsPerWorker /= pipelineDepth;
            var totalPings = pingsPerWorker * workers * pipelineDepth;
            Console.WriteLine();
            Console.WriteLine(Me());
            Console.WriteLine($"{workers} clients, {pingsPerWorker}x{pipelineDepth} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(sPayload ?? "")} bytes");

            RedisValue payload = sPayload;
            object[] args = string.IsNullOrEmpty(sPayload) ? Array.Empty<object>() : new object[] { payload };
            using var muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
            {
                EndPoints = { ServerEndpoint }
            });
            var db = muxer.GetDatabase();
            
            var tasks = new Task[workers];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => RunClientAsync(db, pingsPerWorker, pipelineDepth, args));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);

            var threads = new Thread[workers];
#pragma warning disable IDE0039 // Use local function
            ThreadStart starter = () => RunClient(db, pingsPerWorker, pipelineDepth, args);
#pragma warning restore IDE0039 // Use local function
            timer = Stopwatch.StartNew();
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(starter);
                threads[i].Start();
            }
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }
            timer.Stop();
            Log("sync", timer.Elapsed, totalPings, payload);
        }

        static string Me([CallerMemberName] string caller = null) => caller;
    }
}
