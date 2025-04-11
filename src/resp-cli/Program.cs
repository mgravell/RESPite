using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using StackExchange.Redis;

Option<string> hostOption = new(
    aliases: ["--host", "-h"],
    description: "Server hostname");
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: ["--port", "-p"],
    description: "Server port");
portOption.SetDefaultValue(6379);

Option<bool> guiOption = new(
    aliases: ["--gui"],
    description: "Use GUI mode")
{
    Arity = ArgumentArity.Zero,
};

Option<string?> userOption = new(
    aliases: ["--user"],
    description: "Username (requires --pass)");

Option<string?> passOption = new(
    aliases: ["--pass", "-a"],
    description: "Password to use when connecting to the server (or RESPCLI_AUTH environment variable");

Option<bool> tlsOption = new(
    aliases: ["--tls"],
    description: "Establish a secure TLS connection")
{
    Arity = ArgumentArity.Zero,
};
tlsOption.SetDefaultValue(false);

Option<string?> cacertOption = new(
    aliases: ["--cacert"],
    description: "Path to CA certificate");

Option<string?> certOption = new(
    aliases: ["--cert"],
    description: "Path to user certificate");

Option<string?> keyOption = new(
    aliases: ["--key"],
    description: "Path to user private key file");

Option<bool> resp3Option = new(
    aliases: ["-3"],
    description: "Start session in RESP3 protocol mode")
{
    Arity = ArgumentArity.Zero,
};
resp3Option.SetDefaultValue(false);

RootCommand rootCommand = new(description: "Connects to a RESP server to issue ad-hoc commands.")
{
    hostOption,
    portOption,
    guiOption,
    userOption,
    passOption,
    tlsOption,
    resp3Option,
    cacertOption,
    certOption,
    keyOption,
};

rootCommand.SetHandler(async ic =>
{
    var host = (string)ic.ParseResult.FindResultFor(hostOption)?.GetValueOrDefault()!;
    var port = (int)ic.ParseResult.FindResultFor(portOption)!.GetValueOrDefault()!;
    var gui = (bool)ic.ParseResult.FindResultFor(guiOption)!.GetValueOrDefault()!;
    var user = (string?)ic.ParseResult.FindResultFor(userOption)?.GetValueOrDefault();
    var pass = (string?)ic.ParseResult.FindResultFor(passOption)?.GetValueOrDefault();
    var tls = (bool)ic.ParseResult.FindResultFor(tlsOption)!.GetValueOrDefault()!;
    var resp3 = (bool)ic.ParseResult.FindResultFor(resp3Option)!.GetValueOrDefault()!;
    var cacert = (string?)ic.ParseResult.FindResultFor(cacertOption)?.GetValueOrDefault();
    var cert = (string?)ic.ParseResult.FindResultFor(certOption)?.GetValueOrDefault();
    var key = (string?)ic.ParseResult.FindResultFor(keyOption)?.GetValueOrDefault();
    try
    {
        if (string.IsNullOrEmpty(pass))
        {
            pass = Environment.GetEnvironmentVariable("RESPCLI_AUTH");
        }

        var ep = Utils.BuildEndPoint(host, port);
        if (gui)
        {
            RespDesktop.Run(host, port, tls, user, pass, resp3);
        }
        else
        {
            using var conn = await Utils.ConnectAsync(host, port, tls, Console.WriteLine);
            if (conn is not null)
            {
                var handshake = Utils.GetHandshake(user, pass, resp3);
                await RespClient.RunClient(conn, handshake);
            }
        }
        ic.ExitCode = 0;
    }
    catch (Exception ex)
    {
        ic.Console.Error.WriteLine(ex.Message);
        ic.ExitCode = -1;
    }
});
return await rootCommand.InvokeAsync(args);
