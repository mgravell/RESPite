using Bedrock.Framework;
using BedrockRespProtocol;
using Microsoft.Extensions.DependencyInjection;
using Resp;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SimpleClient
{
    class Program
    {
        private static readonly EndPoint ServerEndpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        static async Task Main2()
        {
            await ExecuteBedrockAsync(ServerEndpoint, 50000);
            // await ExecuteStackExchangeRedis(endpoint, 1000);
        }

        static async Task Main()
        {
            await ExecuteSocketAsync(ServerEndpoint, 50000);
        }

        static async ValueTask ExecuteSocketAsync(EndPoint endpoint, int count)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(new IPEndPoint(IPAddress.Loopback, 6379));
            using var ns = new NetworkStream(socket, true);

            var connection = RespConnection.Create(socket);

            Stopwatch timer;

            timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                await connection.PingRawAsync();
            }
            timer.Stop();
            Console.WriteLine($"{Me()}: time for {count} ops (async): {timer.ElapsedMilliseconds}ms");

            timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                connection.PingRaw();
            }
            timer.Stop();
            Console.WriteLine($"{Me()}: time for {count} ops (sync): {timer.ElapsedMilliseconds}ms");
        }
        static async Task ExecuteBedrockAsync(EndPoint endpoint, int count)
        {
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var client = new ClientBuilder(serviceProvider)
                .UseSockets()
                .Build();

            await using var connection = await client.ConnectAsync(endpoint);

            var protocol = new RespBedrockProtocol(connection);

            //await protocol.SendAsync(RedisFrame.Ping);
            //using (await protocol.ReadAsync()) { }

            Stopwatch timer;

            timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                await protocol.PingRawAsync();
                //await protocol.SendAsync(RedisFrame.Ping);

                //using var pong = await protocol.ReadAsync();
            }
            timer.Stop();
            Console.WriteLine($"{Me()}: time for {count} ops (val-type): {timer.ElapsedMilliseconds}ms");

            //timer = Stopwatch.StartNew();
            //for (int i = 0; i < count; i++)
            //{
            //    await protocol.PingAsync();
            //    //await protocol.SendAsync(RedisFrame.Ping);

            //    //using var pong = await protocol.ReadAsync();
            //}
            //timer.Stop();
            //Console.WriteLine($"{Me()}: time for {count} ops (ref-type): {timer.ElapsedMilliseconds}ms");


        }

        static async Task ExecuteStackExchangeRedis(EndPoint endpoint, int count)
        {
            using var muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
            {
                EndPoints = { endpoint }
            });
            var server = muxer.GetServer(endpoint);
            await server.PingAsync();

            var timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                await server.PingAsync();
            }
            timer.Stop();
            Console.WriteLine($"{Me()}: time for {count} ops: {timer.ElapsedMilliseconds}ms");
        }

        static string Me([CallerMemberName] string caller = null) => caller;
    }
}
