using Bedrock.Framework;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Pipelines.Sockets.Unofficial;
using Respite;
using Respite.Redis;
using Respite.Bedrock;
using StackExchange.Redis;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RunProfiler
{
    public static class Program
    {
        public static void Main() => BenchmarkRunner.Run<RedisPingPong>();
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class RedisPingPong : IAsyncDisposable
{
    private ConnectionMultiplexer _muxer;
    private IServer _server;
    private RespConnection _bedrock, _socket, _stream;

    private ConnectionContext _connection;

    [BenchmarkCategory("Async")]
    [Benchmark(Baseline = true, Description = nameof(SERedis))]
    public Task SERedisAsync() => _server.PingAsync();

    [BenchmarkCategory("Async")]
    [Benchmark(Description = nameof(Bedrock))]
    public Task BedrockAsync() => _bedrock.PingAsync().AsTask();

    [BenchmarkCategory("Async")]
    [Benchmark(Description = nameof(Socket))]
    public Task SocketAsync() => _socket.PingAsync().AsTask();

    [BenchmarkCategory("Async")]
    [Benchmark(Description = nameof(Stream))]
    public Task StreamAsync() => _stream.PingAsync().AsTask();

    [BenchmarkCategory("Sync")]
    [Benchmark(Baseline = true)]
    public void SERedis() => _server.Ping();

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Bedrock() => _bedrock.Ping();

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Socket() => _socket.Ping();

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Stream() => _stream.Ping();


    public ValueTask DisposeAsync()
    {
        _muxer?.Dispose();
        _stream?.Dispose();
        _socket?.Dispose();
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
        SERedis();
        await SERedisAsync();

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var client = new ClientBuilder(serviceProvider)
            .UseSockets()
            .Build();

        _connection = await client.ConnectAsync(endpoint);
        _bedrock = new RespBedrockProtocol(_connection);
        Bedrock();
        await BedrockAsync();

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        SocketConnection.SetRecommendedClientOptions(socket);
        socket.Connect(endpoint);
        _socket = RespConnection.Create(socket);
        Socket();
        await SocketAsync();

        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        SocketConnection.SetRecommendedClientOptions(socket);
        socket.Connect(endpoint);
        _stream = RespConnection.Create(new NetworkStream(socket));
        Stream();
        await StreamAsync();
    }
}