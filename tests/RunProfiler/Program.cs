using Bedrock.Framework;
using BedrockRespProtocol;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Resp;
using StackExchange.Redis;
using System;
using System.Net;
using System.Threading.Tasks;

namespace RunProfiler
{
    public static class Program
    {
        public static void Main() => BenchmarkRunner.Run<RedisPingPong>();
    }
}

[MemoryDiagnoser]
public class RedisPingPong : IAsyncDisposable
{
    private ConnectionMultiplexer _muxer;
    private IServer _server;
    private RespClientProtocol _protocol;
    private ConnectionContext _connection;

    [Benchmark]
    public Task SERedis() => _server.PingAsync();

    [Benchmark]
    public Task Bedrock() => BedrockPingPong();

    async Task BedrockPingPong()
    {
        await _protocol.SendAsync(RedisFrame.Ping);

        using var pong = await _protocol.ReadAsync();
    }

    public ValueTask DisposeAsync()
    {
        _muxer?.Dispose();
        return _connection == null ? default : _connection.DisposeAsync();
    }

    [GlobalSetup]
    public async Task ConnectAsync()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        _muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { endpoint }
        });
        _server = _muxer.GetServer(endpoint);
        await _server.PingAsync();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var client = new ClientBuilder(serviceProvider)
            .UseSockets()
            .Build();

        _connection = await client.ConnectAsync(endpoint);

        _protocol = new RespClientProtocol(_connection);

        await BedrockPingPong();
    }
}