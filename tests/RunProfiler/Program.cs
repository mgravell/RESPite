using Bedrock.Framework;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Pipelines.Sockets.Unofficial;
using Respite;
using Respite.Bedrock;
using StackExchange.Redis;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
    public Task BedrockAsync() => PingAsync(_bedrock);

    [BenchmarkCategory("Async")]
    [Benchmark(Description = nameof(Socket))]
    public Task SocketAsync() => PingAsync(_socket);

    [BenchmarkCategory("Async")]
    [Benchmark(Description = nameof(Stream))]
    public Task StreamAsync() => PingAsync(_stream);

    static readonly RespValue
        s_ping = RespValue.CreateAggregate(RespType.Array, "PING"),
        s_pong = RespValue.Create(RespType.SimpleString, "PONG");

    static Task PingAsync(RespConnection connection)
        => connection.CallAsync(s_ping, resp =>
        {
            resp.ThrowIfError();
            if (!resp.Equals(s_pong)) Throw();
        }).AsTask();
    static void Ping(RespConnection connection)
        => connection.Call(s_ping, resp =>
        {
            resp.ThrowIfError();
            if (!resp.Equals(s_pong)) Throw();
        });

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Throw() => throw new InvalidOperationException();

    [BenchmarkCategory("Sync")]
    [Benchmark(Baseline = true)]
    public void SERedis() => _server.Ping();

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Bedrock() => Ping(_bedrock);

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Socket() => Ping(_socket);

    [BenchmarkCategory("Sync")]
    [Benchmark]
    public void Stream() => Ping(_stream);


    public async ValueTask DisposeAsync()
    {
        _muxer?.Dispose();
        if (_stream != null) await _stream.DisposeAsync().ConfigureAwait(false);
        if (_socket != null) await _socket.DisposeAsync().ConfigureAwait(false);
        if (_connection != null) await _connection.DisposeAsync().ConfigureAwait(false);
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