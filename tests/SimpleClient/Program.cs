using Bedrock.Framework;
using BedrockRespProtocol;
using Microsoft.Extensions.DependencyInjection;
using Resp;
using System;
using System.Net;
using System.Threading.Tasks;

namespace SimpleClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var client = new ClientBuilder(serviceProvider)
                .UseSockets()
                .Build();

            var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
            Console.WriteLine($"Connecting to {endpoint}...");
            await using var connection = await client.ConnectAsync(endpoint);
            Console.WriteLine("Connected");

            var protocol = new RespClientProtocol(connection);

            await protocol.SendAsync(RedisFrame.Ping);
        }
    }
}
