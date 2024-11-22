#pragma warning disable CS8321 // Local function is declared but never used

using System.Diagnostics;
using System.Net;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Client;
using RESPite.Transports;
using StackExchange.Redis;

const int DefaultTargetOps = 100_000;

var ep = new IPEndPoint(IPAddress.Loopback, 6379);
// using var respite = ep.CreateTransport().RequestResponse(RespFrameScanner.Default);
using var respite = ep.CreateTransport().WithOutboundBuffer().Multiplexed(RespFrameScanner.Default);
using var muxer = ConnectionMultiplexer.Connect(new ConfigurationOptions { EndPoints = { ep } });
var seredis = muxer.GetDatabase();

var stringKey = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString());
var listKey = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString());
var payload512 = new byte[512];
var rand = new Random();
rand.NextBytes(payload512);

int expirySeconds = 10 * 60;
TimeSpan expiryTime = TimeSpan.FromSeconds(expirySeconds);

seredis.StringSet(stringKey, payload512, expiryTime); // in case get-only
var payload50 = new byte[50];
for (int i = 0; i < 15; i++)
{
    rand.NextBytes(payload50);
    seredis.ListLeftPush(listKey, payload50);
}
seredis.KeyExpire(listKey, expiryTime);

Mode[] modes = [Mode.Ping, Mode.Set, Mode.Get, Mode.List];
int[] workerCounts = [1, 5, 10, 20, 40];

foreach (var mode in modes)
{
    Console.WriteLine($"# MODE: {mode}");
    Console.WriteLine();
    foreach (int workerCount in workerCounts)
    {
        RunRESPite(workerCount, mode);
    }
    Console.WriteLine();
    foreach (int workerCount in workerCounts)
    {
        await RunRESPiteAsync(workerCount, mode);
    }
    Console.WriteLine();
    foreach (int workerCount in workerCounts)
    {
        RunSERedis(workerCount, mode);
    }
    Console.WriteLine();
    foreach (int workerCount in workerCounts)
    {
        await RunSERedisAsync(workerCount, mode);
    }
    Console.WriteLine();
}

async Task RunRESPiteAsync(int workerCount, Mode mode, int targetOps = DefaultTargetOps)
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

        switch (mode)
        {
            case Mode.Ping:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    await Server.PING.SendAsync(respite).ConfigureAwait(false);
                }
                break;
            case Mode.Get:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    (await Strings.GET.SendAsync(respite, stringKey).ConfigureAwait(false)).Dispose();
                }
                break;
            case Mode.Set:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    await Strings.SET.SendAsync(respite, (stringKey, payload512)).ConfigureAwait(false);
                }
                break;
            case Mode.List:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    (await Lists.LRANGE.SendAsync(respite, (listKey, 0, 10))).Dispose();
                }
                break;
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

void RunRESPite(int workerCount, Mode mode, int targetOps = DefaultTargetOps)
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

        switch (mode)
        {
            case Mode.Ping:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    Server.PING.Send(respite);
                }
                break;
            case Mode.Get:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    Strings.GET.Send(respite, stringKey).Dispose();
                }
                break;
            case Mode.Set:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    Strings.SET.Send(respite, (stringKey, payload512));
                }
                break;
            case Mode.List:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    Lists.LRANGE.Send(respite, (listKey, 0, 10)).Dispose();
                }
                break;
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

async Task RunSERedisAsync(int workerCount, Mode mode, int targetOps = DefaultTargetOps)
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
        switch (mode)
        {
            case Mode.Ping:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    await seredis.PingAsync().ConfigureAwait(false);
                }
                break;
            case Mode.Get:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    (await seredis.StringGetLeaseAsync(stringKey).ConfigureAwait(false))?.Dispose();
                }
                break;
            case Mode.Set:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    await seredis.StringSetAsync(stringKey, payload512, expiryTime).ConfigureAwait(false);
                }
                break;
            case Mode.List:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    await seredis.ListRangeAsync(listKey, 0, 10).ConfigureAwait(false);
                }
                break;
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

void RunSERedis(int workerCount, Mode mode, int targetOps = DefaultTargetOps)
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
        switch (mode)
        {
            case Mode.Ping:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    seredis.Ping();
                }
                break;
            case Mode.Get:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    seredis.StringGetLease(stringKey)?.Dispose();
                }
                break;
            case Mode.Set:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    seredis.StringSet(stringKey, payload512, expiryTime);
                }
                break;
            case Mode.List:
                for (int i = 0; i < OPS_THIS_RUN; i++)
                {
                    seredis.ListRange(listKey, 0, 10);
                }
                break;
        }
        Interlocked.Add(ref totalOps, OPS_THIS_RUN);
    }
}

internal enum Mode
{
    Ping,
    Get,
    Set,
    List,
}
