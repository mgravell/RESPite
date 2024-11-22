#pragma warning disable CS8321 // Local function is declared but never used

using System.Diagnostics;
using System.Net;
using RESPite.Resp;
using RESPite.Resp.Client;
using RESPite.Transports;
using StackExchange.Redis;

var ep = new IPEndPoint(IPAddress.Loopback, 6379);
// using var respite = ep.CreateTransport().RequestResponse(RespFrameScanner.Default);
using var respite = ep.CreateTransport().WithOutboundBuffer().Multiplexed(RespFrameScanner.Default);
using var muxer = ConnectionMultiplexer.Connect(new ConfigurationOptions { EndPoints = { ep } });
var seredis = muxer.GetServers().First();

RunRESPite(40, 250_000);
/*
for (int i = 0; i < 5; i++)
{
    RunRESPite(40, 250_000);
    // RunSERedis(40, 250_000);
}
*/

/*
int[] workerCounts = [1, 5, 10, 20, 40];
foreach (int workerCount in workerCounts)
{
    RunRESPite(workerCount);
}
Console.WriteLine();
foreach (int workerCount in workerCounts)
{
    await RunRESPiteAsync(workerCount);
}
Console.WriteLine();
foreach (int workerCount in workerCounts)
{
    RunSERedis(workerCount);
}
Console.WriteLine();
foreach (int workerCount in workerCounts)
{
    await RunSERedisAsync(workerCount);
}
Console.WriteLine();
*/

async Task RunRESPiteAsync(int workerCount, int targetOps = 100_000)
{
    int remaining = workerCount;
    int totalOps = 0;
    Task[] workers = new Task[remaining];

    Stopwatch timer = Stopwatch.StartNew();
    for (int i = 0; i < workers.Length; i++)
    {
        int snapshot = i;
        workers[i] = Task.Run(() => RunAsync(snapshot));
    }

    for (int i = 0; i < workers.Length; i++)
    {
        await workers[i];
    }
    timer.Stop();
    Console.WriteLine($"RESPite async; {totalOps} over {workers.Length} workers in {timer.ElapsedMilliseconds}ms; {totalOps / timer.Elapsed.TotalSeconds}ops/s");

    async Task RunAsync(int label)
    {
        int OPS_THIS_RUN = targetOps / workerCount;
        for (int i = 0; i < OPS_THIS_RUN; i++)
        {
            await Server.PING.SendAsync(respite).ConfigureAwait(false);
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

void RunRESPite(int workerCount, int targetOps = 100_000)
{
    object gate = new object();
    int remaining = workerCount;
    int totalOps = 0;
    Thread[] workers = new Thread[remaining];

    Stopwatch timer = new Stopwatch();
    for (int i = 0; i < workers.Length; i++)
    {
        int snapshot = i;
        (workers[i] = new Thread(() => Run(snapshot))).Start();
    }

    for (int i = 0; i < workers.Length; i++)
    {
        workers[i].Join();
    }
    timer.Stop();
    Console.WriteLine($"RESPite sync; {totalOps} over {workers.Length} workers in {timer.ElapsedMilliseconds}ms; {totalOps / timer.Elapsed.TotalSeconds}ops/s");

    void Run(int label)
    {
        lock (gate)
        {
            if (--remaining == 0)
            {
                timer.Restart();
                Monitor.PulseAll(gate);
            }
            else
            {
                Monitor.Wait(gate);
            }
        }
        int OPS_THIS_RUN = targetOps / workerCount;
        for (int i = 0; i < OPS_THIS_RUN; i++)
        {
            Server.PING.Send(respite);
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

async Task RunSERedisAsync(int workerCount, int targetOps = 100_000)
{
    int remaining = workerCount;
    int totalOps = 0;
    Task[] workers = new Task[remaining];

    Stopwatch timer = Stopwatch.StartNew();
    for (int i = 0; i < workers.Length; i++)
    {
        int snapshot = i;
        workers[i] = Task.Run(() => RunAsync(snapshot));
    }

    for (int i = 0; i < workers.Length; i++)
    {
        await workers[i];
    }
    timer.Stop();
    Console.WriteLine($"SE.Redis async; {totalOps} over {workers.Length} workers in {timer.ElapsedMilliseconds}ms; {totalOps / timer.Elapsed.TotalSeconds}ops/s");

    async Task RunAsync(int label)
    {
        int OPS_THIS_RUN = targetOps / workerCount;
        for (int i = 0; i < OPS_THIS_RUN; i++)
        {
            await seredis.PingAsync().ConfigureAwait(false);
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

void RunSERedis(int workerCount, int targetOps = 100_000)
{
    object gate = new object();
    int remaining = workerCount;
    int totalOps = 0;
    Thread[] workers = new Thread[remaining];

    Stopwatch timer = new Stopwatch();
    for (int i = 0; i < workers.Length; i++)
    {
        int snapshot = i;
        (workers[i] = new Thread(() => Run(snapshot))).Start();
    }

    for (int i = 0; i < workers.Length; i++)
    {
        workers[i].Join();
    }
    timer.Stop();
    Console.WriteLine($"SE.Redis sync; {totalOps} over {workers.Length} workers in {timer.ElapsedMilliseconds}ms; {totalOps / timer.Elapsed.TotalSeconds}ops/s");

    void Run(int label)
    {
        lock (gate)
        {
            if (--remaining == 0)
            {
                timer.Restart();
                Monitor.PulseAll(gate);
            }
            else
            {
                Monitor.Wait(gate);
            }
        }
        int OPS_THIS_RUN = targetOps / workerCount;
        for (int i = 0; i < OPS_THIS_RUN; i++)
        {
            seredis.Ping();
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}
