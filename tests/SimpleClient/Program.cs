using Bedrock.Framework;
using BedrockRespProtocol;
using Microsoft.Extensions.DependencyInjection;
using Pipelines.Sockets.Unofficial;
using Resp;
using StackExchange.Redis;
using System;
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
        static void Main2()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketConnection.SetRecommendedClientOptions(socket);
            socket.Connect(ServerEndpoint);
            using var client = RespConnection.Create(socket);
            var payload = new string('a', 2048);
            var frame = RespFrame.Create(FrameType.Array, "ping", payload);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                client.Send(frame);
                var reply = client.Receive();
                // client.Ping();
            }
            timer.Stop();
            Log("sync", timer.Elapsed, 1000, payload);
        }
        static async Task Main()
        {
            const int CLIENTS = 10, PER_CLIENT = 1000;

            var payload = new string('a', 2048);
            for (int i = 0; i < 3; i++)
            {
                await ExecuteSocketAsync(PER_CLIENT, CLIENTS, payload);
                await ExecuteNetworkStreamAsync(PER_CLIENT, CLIENTS, payload);
                await ExecuteStackExchangeRedisAsync(PER_CLIENT, CLIENTS, payload);
                await ExecuteBedrockAsync(PER_CLIENT, CLIENTS, payload);
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

        static async Task RunClientAsync(RespConnection client, int pingsPerClient, string payload)
        {
            var frame = RespFrame.Create(FrameType.Array, "ping", payload);
            for (int i = 0; i < pingsPerClient; i++)
            {
                await client.SendAsync(frame).ConfigureAwait(false);
                var reply = await client.ReceiveAsync().ConfigureAwait(false);
                // await client.PingAsync();
            }
        }
        static async Task RunClientAsync(IServer client, int pingsPerClient, RedisValue payload)
        {
            for (int i = 0; i < pingsPerClient; i++)
            {
                await client.EchoAsync(payload).ConfigureAwait(false);
                // await client.PingAsync();
            }
        }
        static void RunClient(RespConnection client, int pingsPerClient, string payload)
        {
            var frame = RespFrame.Create(FrameType.Array, "ping", payload);
            for (int i = 0; i < pingsPerClient; i++)
            {
                client.Send(frame);
                var reply = client.Receive();
                // client.Ping();
            }
        }
        static void RunClient(IServer client, int pingsPerClient, RedisValue payload)
        {
            for (int i = 0; i < pingsPerClient; i++)
            {
                client.Echo(payload);
            }
        }
        static void Log(string label, TimeSpan elapsed, long count, string payload)
        {
            var MiB = (count * 2 * Encoding.UTF8.GetByteCount(payload)) / (double)(1024 * 1024);
            Console.WriteLine($"{label}: {(int)elapsed.TotalMilliseconds}ms, {count / elapsed.TotalSeconds:###,##0} ops/s, {MiB / elapsed.TotalSeconds:###,##0.00} MiB/s");
        }

        static ValueTask ExecuteSocketAsync(int pingsPerClient, int clientCount, string payload)
            => ExecuteDirectAsync(pingsPerClient, clientCount, payload, false);

        static ValueTask ExecuteNetworkStreamAsync(int pingsPerClient, int clientCount, string payload)
            => ExecuteDirectAsync(pingsPerClient, clientCount, payload, true);

        static async ValueTask ExecuteDirectAsync(int pingsPerClient, int clientCount, string payload, bool asNetworkStream, [CallerMemberName]string caller = null)
        {
            var totalPings = pingsPerClient * clientCount;
            Console.WriteLine();
            Console.WriteLine(caller);
            Console.WriteLine($"{clientCount} clients, {pingsPerClient} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(payload)} bytes");

            var clients = CreateClients(clientCount, asNetworkStream);
            await RunClientAsync(clients[0], 1, payload);
            var tasks = new Task[clientCount];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                var client = clients[i];
                tasks[i] = Task.Run(() => RunClientAsync(client, pingsPerClient, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);
            
            var threads = new Thread[clientCount];
            ParameterizedThreadStart starter = state => RunClient((RespConnection)state, pingsPerClient, payload);
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

        static async Task ExecuteBedrockAsync(int pingsPerClient, int clientCount, string payload)
        {
            var totalPings = pingsPerClient * clientCount;
            Console.WriteLine();
            Console.WriteLine(Me());
            Console.WriteLine($"{clientCount} clients, {pingsPerClient} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(payload)} bytes");
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
                tasks[i] = Task.Run(() => RunClientAsync(client, pingsPerClient, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);

            var threads = new Thread[clientCount];
            ParameterizedThreadStart starter = state => RunClient((RespConnection)state, pingsPerClient, payload);
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

        static async Task ExecuteStackExchangeRedisAsync(int pingsPerWorker, int workers, string sPayload)
        {
            int totalPings = pingsPerWorker * workers;
            Console.WriteLine();
            Console.WriteLine(Me());
            Console.WriteLine($"{workers} clients, {pingsPerWorker} pings each, total {totalPings}");
            Console.WriteLine($"payload: {Encoding.UTF8.GetByteCount(sPayload)} bytes");

            RedisValue payload = sPayload;

            using var muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
            {
                EndPoints = { ServerEndpoint }
            });
            var server = muxer.GetServer(ServerEndpoint);
            
            var tasks = new Task[workers];
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => RunClientAsync(server, pingsPerWorker, payload));
            }
            await Task.WhenAll(tasks);
            timer.Stop();
            Log("async", timer.Elapsed, totalPings, payload);

            var threads = new Thread[workers];
            ThreadStart starter = () => RunClient(server, pingsPerWorker, payload);
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
