using System.Net;
using RESPite.Resp;
using RESPite.Resp.Client;
using RESPite.Transports;

var ep = new IPEndPoint(IPAddress.Loopback, 6379);
// using var transport = ep.CreateTransport().RequestResponse(RespFrameScanner.Default);
using var transport = ep.CreateTransport().Multiplexed(RespFrameScanner.Default);

Console.WriteLine("pinging server...");
for (int i = 0; i < 50000; i++)
{
    // Server.PING.Send(transport);
    await Server.PING.SendAsync(transport).ConfigureAwait(false);
    if ((i % 5000) == 0) Console.WriteLine(i);
}
Console.WriteLine("done");
