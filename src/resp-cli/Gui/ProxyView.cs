using System.Net;
using System.Net.Sockets;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal class ProxyView : TabBase
{
    private readonly TextView log;
    private readonly ConnectionOptionsBag options;
    private readonly CancellationToken endOfLife;

    public ProxyView(ConnectionOptionsBag options, CancellationToken endOfLife)
    {
        this.options = options;
        this.endOfLife = endOfLife;

        SetStatus("Debugging proxy server");
        log = new TextView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
        };
        Add(log);

        _ = Task.Run(() => StartProxyServer());
    }

    private static string Format(EndPoint? endpoint) => endpoint switch
    {
        null => "",
        IPEndPoint ip when ip.AddressFamily == AddressFamily.InterNetwork => $"{ip.Address}:{ip.Port}",
        IPEndPoint ip => $"[{ip.Address}]:{ip.Port}",
        DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        _ => endpoint.ToString() ?? "",
    };

    public event Action<ConnectionOptionsBag>? AutoConnect;
    private async Task StartProxyServer()
    {
        try
        {
            var ep = new IPEndPoint(IPAddress.Loopback, options.ProxyPort);
            log.Append($"Starting debugging proxy server on {Format(ep)}");
            log.Append($"Server: {options.Host} on port {options.Port}");
            log.Append($"Binding socket and entering listen mode...");
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(ep);
            socket.Listen(10);

            bool selfConnect = options.DebugProxyServer;
            while (true)
            {
                log.Append("Waiting for client...");

                if (selfConnect)
                {
                    selfConnect = false;
                    _ = Task.Run(() => AutoConnect?.Invoke(new ConnectionOptionsBag
                    {
                        Port = options.ProxyPort,
                        Resp3 = options.Resp3,
                        Database = options.Database,
                        User = options.User,
                        Password = options.Password,
                        Handshake = options.Handshake,
                    }));
                }

                var client = await socket.AcceptAsync(endOfLife);
                log.Append($"Accepted client: {Format(client.RemoteEndPoint)}");
                client.NoDelay = true;
                var evt = ClientConnected;
                if (evt == null)
                {
                    client.Dispose();
                    log.Append("(nothing to do!)");
                    break;
                }
                try
                {
                    evt(new NetworkStream(client));
                }
                catch (Exception ex)
                {
                    log.Append(ex.Message);
                    client.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            log.Append(ex.Message);
        }
        finally
        {
            log.Append("(server terminated)");
        }
    }

    public Action<Stream>? ClientConnected { get; set; }
}
