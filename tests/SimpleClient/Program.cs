using Microsoft.Extensions.DependencyInjection;
using Pipelines.Sockets.Unofficial;
using Respite;
using Respite.Redis;
using ServiceStack.Redis;
using StackExchange.Redis;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.StackExchange.Redis;
using System.Linq;

#if NETCOREAPP3_1
using Bedrock.Framework;
using Respite.Bedrock;
#endif

namespace SimpleClient
{
    class Program
    {
        private static readonly EndPoint ServerEndpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        private static readonly string ServerEndpointString = "127.0.0.1:6379";


        static Task Main() => SERedisShim();

        //SocketConnection.SetRecommendedClientOptions(socket);

        static async Task SERedisShim()
        {
            var config = ConfigurationOptions.Parse(ServerEndpointString);
            const int POOL_SIZE = 10;
            using var pooled = await config.GetPooledMultiplexerAsync(POOL_SIZE);
            using var multiplexed = await ConnectionMultiplexer.ConnectAsync(config);

            Console.WriteLine("Warming up...");// JIT
            await TestConcurrentClients(null, multiplexed, 1, 2, 1);
            await TestConcurrentClients(null, multiplexed, 1, 2, 2);
            await TestConcurrentClients(null, pooled, 1, 2, 1);
            await TestConcurrentClients(null, pooled, 1, 2, 2);

            
            const int WORKERS = 10, PER_WORKER = 2000;
            _ = WORKERS;
            string pooledName = $"Pooled x{POOL_SIZE}";
            int[] depths = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 50, 100, 200 };
            //Console.WriteLine("Profiling without congestion (1 worker)...");
            //foreach (int depth in depths)
            //{
            //    await TestConcurrentClients("Multiplexed", multiplexed, 1, PER_WORKER, depth);
            //    await TestConcurrentClients(pooledName, pooled, 1, PER_WORKER, depth);
            //}
            Console.WriteLine($"Profiling with congestion ({WORKERS} workers)...");
            foreach (int depth in depths)
            {
                await TestConcurrentClients("Multiplexed", multiplexed, WORKERS, PER_WORKER, depth);
            }
            Console.WriteLine();
            foreach (int depth in depths)
            {
                await TestConcurrentClients(pooledName + "/d", pooled, WORKERS, PER_WORKER, depth, false);
                if (depth == 1)
                {
                    await TestConcurrentClients(pooledName + "/l", pooled, WORKERS, PER_WORKER, depth, true);
                }
            }
        }

        static async Task TestConcurrentClients(string label, IConnectionMultiplexer muxer, int workers, int perWorker, int depth, bool lease = false)
        {
            var db = muxer.GetDatabase();
            RedisKey key = Guid.NewGuid().ToString("N");
            var tasks = new Task[workers];
            var watch = Stopwatch.StartNew();
            perWorker /= depth;
            for (int i = 0; i < workers; i++)
            {
                async Task DoTheThing(IDatabase db)
                {
                    var tasks = new Task[depth];
                    for (int j = 0; j < perWorker; j++)
                    {
                        if (depth == 1)
                            await db.StringIncrementAsync(key);
                        else
                        {
                            var batch = db.CreateBatch();
                            for (int d = 0; d < depth; d++)
                                tasks[d] = batch.StringIncrementAsync(key);
                            batch.Execute();
                            await Task.WhenAll(tasks);
                        }
                    }
                }
                if (lease)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        await using var tmp = await db.LeaseDedicatedAsync();
                        await DoTheThing(tmp.Value);
                    });
                }
                else
                {
                    tasks[i] = Task.Run(() => DoTheThing(db));
                }
            }
            await Task.WhenAll(tasks);
            watch.Stop();
            var total = (long)await db.StringGetAsync(key);
            await db.KeyDeleteAsync(key);
            var expected = workers * perWorker * depth;
            if (!string.IsNullOrWhiteSpace(label))
            {
                if (total != expected) Console.WriteLine($"warning: expected {expected}, actual {total}");
                Console.WriteLine($"{label}, {workers}x{perWorker}x{depth}\t{watch.ElapsedMilliseconds}ms\t{total / watch.Elapsed.TotalSeconds:F2} op/s");
            }
        }

        static async Task TestArdb()
        {
            await using var redis = await RedisConnection.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, 16379));

            await redis.CallAsync("set", "foo", "bar", "ex", 5);
            Console.WriteLine(redis.CallAsync("get", "foo"));
        }
        static async Task TestResp3()
        {
            await using var redis = await RedisConnection.ConnectAsync(ServerEndpoint);

            // bump the server into RESP 3
            Dump(await redis.CallAsync("hello", 3));

            // simple list
            await redis.CallAsync("del", "foo");
            await redis.CallAsync("lpush", "foo", 123);
            await redis.CallAsync("lpush", "foo", 456);
            Dump(await redis.CallAsync("llen", "foo"));
            Dump(await redis.CallAsync("lrange", "foo", 0, -1));

            // counter (note: to redis this is still a string)
            await redis.CallAsync("del", "counter");
            await redis.CallAsync("incrbyfloat", "counter", 42.5);
            await redis.CallAsync("incrbyfloat", "counter", -3);
            Dump(await redis.CallAsync("get", "counter"));

            // map; note this behaves differently between RESPs
            await redis.CallAsync("del", "bar");
            await redis.CallAsync("hset", "bar", "name", "Marc", "shoe-size", 10);
            Dump(await redis.CallAsync("hgetall", "bar"));
        }

        private static void Dump(object obj, string prefix = "")
        {
            switch (obj)
            {
                case null:
                    Console.WriteLine($"{prefix}(null)");
                    break;
                case object[] arr:
                    Console.WriteLine($"{prefix}array [{arr.Length}]");
                    foreach (var el in arr) Dump(el, "> " + prefix);
                    break;
                case ISet<object> set:
                    Console.WriteLine($"{prefix}set [{set.Count}]");
                    foreach (var el in set) Dump(el, "> " + prefix);
                    break;
                case IDictionary<object, object> map:
                    Console.WriteLine($"{prefix}map [{map.Count}]");
                    foreach (var pair in map)
                    {
                        Dump(pair.Key, $"{prefix}(key)> ");
                        Dump(pair.Value, $"{prefix}(val)> ");
                    }
                    break;
                default:
                    Console.WriteLine($"{prefix}{obj} ({obj.GetType().Name})");
                    break;
            }
            if (string.IsNullOrEmpty(prefix))
                Console.WriteLine();
        }


#pragma warning disable IDE0051 // Remove unused private members
        static async ValueTask BasicTest()
#pragma warning restore IDE0051 // Remove unused private members
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Connect(ServerEndpoint);
            await using var client = RespConnection.Create(socket);
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
        static async Task BasicBenchmark()
#pragma warning restore IDE0051 // Remove unused private members
        {
            const int CLIENTS = 100, PER_CLIENT = 1000, PIPELINE_DEPTH = 20;
            int[] POOL_SIZES = { 1, 2, 3, 5, 10, 20, 30 };
            string payload = null; // "abc"; //  new string('a', 2048);
            for (int i = 0; i < 3; i++)
            {

                foreach (int poolSize in POOL_SIZES)
                {
                    await ExecutePooledNetworkStreamAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload, poolSize);
                }
                //await ExecutePooledSocketAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                //await ExecuteNetworkStreamAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                //await ExecuteSocketAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
#if NETCOREAPP3_1
                //await ExecuteBedrockAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
#endif
                await ExecuteStackExchangeRedisAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                //if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERVICESTACK_LICENSE")))
                //{
                //    await ExecuteServiceStackRedisAsync(PER_CLIENT, CLIENTS, PIPELINE_DEPTH, payload);
                //}
            }
        }

        static RespConnection[] CreateClients(int count, bool asNetworkStream)
        {
            var clients = new RespConnection[count];

            for (int i = 0; i < clients.Length; i++)
            {
                var connection = CreateClient(asNetworkStream);
                clients[i] = connection;
            }

            return clients;
        }

        static RespConnection CreateClient(bool asNetworkStream)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Connect(ServerEndpoint);
            var connection = asNetworkStream ? RespConnection.Create(new NetworkStream(socket)) : RespConnection.Create(socket);
            // connection.Ping();
            return connection;
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

        static async Task RunClientAsync(RespConnectionPool pool, int pingsPerClient, int pipelineDepth, string payload)
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
                    await pool.CallAsync(frame, result =>
                    {
                        result.ThrowIfError();
                        if (!result.Equals(expected)) Throw();
                    });
                    // await client.PingAsync();
                }
            }
            else
            {
                using var frames = Replicate(frame, pipelineDepth);
                for (int i = 0; i < pingsPerClient; i++)
                {
                    using var batch = await pool.BatchAsync(frames.Value).ConfigureAwait(false);
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

        static void RunClient(RedisManagerPool pool, int pingsPerClient, int pipelineDepth, object[] args)
        {
            using var client = pool.GetClient();
            for (int i = 0; i < pingsPerClient; i++)
            {
                if (pipelineDepth == 1)
                {
                    client.Ping();
                }
                else
                {
                    using var batch = client.CreatePipeline();
                    for (int j = 0; j < pipelineDepth; j++)
                    {
                        batch.QueueCommand(x => x.Ping());
                    }
                    batch.Flush();
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

        static ValueTask ExecutePooledSocketAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload, int maxCount)
            => ExecutePooledDirectAsync(pingsPerClient, clientCount, pipelineDepth, payload, false, maxCount);

        static ValueTask ExecutePooledNetworkStreamAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload, int maxCount)
            => ExecutePooledDirectAsync(pingsPerClient, clientCount, pipelineDepth, payload, true, maxCount);
        static async ValueTask ExecutePooledDirectAsync(int pingsPerClient, int clientCount, int pipelineDepth, string payload, bool asNetworkStream, int maxCount, [CallerMemberName]string caller = null)
        {
            pingsPerClient /= pipelineDepth;
            var totalPings = pingsPerClient * clientCount * pipelineDepth;
            Console.WriteLine();
            Console.WriteLine(caller);
            Console.WriteLine($"{clientCount} clients, {pingsPerClient}x{pipelineDepth} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(payload ?? "")} bytes");

            await using var pool = new RespConnectionPool(
                ct => new ValueTask<RespConnection>(CreateClient(asNetworkStream)),
                RespConnectionPoolOptions.Create(maxCount));

            var tasks = new Task[clientCount];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => RunClientAsync(pool, pingsPerClient, pipelineDepth, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);

            Console.WriteLine($"Pool count: {pool.ConnectionCount} ({pool.TotalConnectionCount})");

            //            var threads = new Thread[clientCount];
            //#pragma warning disable IDE0039 // Use local function
            //            ParameterizedThreadStart starter = state => RunClient((RespConnection)state, pingsPerClient, pipelineDepth, payload);
            //#pragma warning restore IDE0039 // Use local function
            //            timer = Stopwatch.StartNew();
            //            for (int i = 0; i < threads.Length; i++)
            //            {
            //                threads[i] = new Thread(starter);
            //                threads[i].Start(pool);
            //            }
            //            for (int i = 0; i < threads.Length; i++)
            //            {
            //                threads[i].Join();
            //            }
            //            timer.Stop();
            //            Log("sync", timer.Elapsed, totalPings, payload);
        }
#if NETCOREAPP3_1
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
#endif

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

        static async Task ExecuteServiceStackRedisAsync(int pingsPerWorker, int workers, int pipelineDepth, string sPayload)
        {
            await Task.Yield();
            pingsPerWorker /= pipelineDepth;
            var totalPings = pingsPerWorker * workers * pipelineDepth;
            Console.WriteLine();
            Console.WriteLine(Me());
            Console.WriteLine($"{workers} clients, {pingsPerWorker}x{pipelineDepth} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(sPayload ?? "")} bytes");

            RedisValue payload = sPayload;
            object[] args = string.IsNullOrEmpty(sPayload) ? Array.Empty<object>() : new object[] { payload };
            using var manager = new RedisManagerPool(ServerEndpointString);

            //var tasks = new Task[workers];
            //Stopwatch timer = Stopwatch.StartNew();
            //for (int i = 0; i < tasks.Length; i++)
            //{
            //    tasks[i] = Task.Run(() => RunClientAsync(manager, pingsPerWorker, pipelineDepth, args));
            //}
            //await Task.WhenAll(tasks);
            //timer.Stop();
            //Log("async", timer.Elapsed, totalPings, payload);

            var threads = new Thread[workers];
#pragma warning disable IDE0039 // Use local function
            ThreadStart starter = () => RunClient(manager, pingsPerWorker, pipelineDepth, args);
#pragma warning restore IDE0039 // Use local function
            var timer = Stopwatch.StartNew();
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
