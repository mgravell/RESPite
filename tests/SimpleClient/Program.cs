using Bedrock.Framework;
using BedrockRespProtocol;
using Microsoft.Extensions.DependencyInjection;
using Resp;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SimpleClient
{
    class Program
    {
        static async Task Main()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
            await ExecuteBedrock(endpoint, 1000);
            await ExecuteStackExchangeRedis(endpoint, 1000);
        }

        static async Task ExecuteBedrock(EndPoint endpoint, int count)
        {
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var client = new ClientBuilder(serviceProvider)
                .UseSockets()
                .Build();

            await using var connection = await client.ConnectAsync(endpoint);

            var protocol = new RespClientProtocol(connection);

            await protocol.SendAsync(RedisFrame.Ping);
            using (await protocol.ReadAsync()) { }

            var timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                await protocol.SendAsync(RedisFrame.Ping);

                using var pong = await protocol.ReadAsync();
            }
            timer.Stop();
            Console.WriteLine($"{Me()}: time for {count} ops: {timer.ElapsedMilliseconds}ms");
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
