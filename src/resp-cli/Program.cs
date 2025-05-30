﻿using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StackExchange.Redis;

Option<string> hostOption = new(
    aliases: ["--host", "-h"],
    description: "Server hostname")
{
    ArgumentHelpName = "hostname",
};
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: ["--port", "-p"],
    description: "Server port")
{
    ArgumentHelpName = "port",
};
portOption.SetDefaultValue(6379);

Option<bool> guiOption = new(
    aliases: ["--gui"],
    description: "Use GUI mode")
{
    Arity = ArgumentArity.Zero,
};

Option<string?> userOption = new(
    aliases: ["--user"],
    description: "Username (requires --pass)")
{
    ArgumentHelpName = "username",
};

Option<string?> passOption = new(
    aliases: ["--pass", "-a"],
    description: "Password to use when connecting to the server (or RESPCLI_AUTH environment variable)")
{
    ArgumentHelpName = "password",
};

Option<bool> tlsOption = new(
    aliases: ["--tls"],
    description: "Establish a secure TLS connection")
{
    Arity = ArgumentArity.Zero,
};

Option<string?> cacertOption = new(
    aliases: ["--cacert"],
    description: "CA Certificate file to verify with")
{
    ArgumentHelpName = "file",
};

Option<string?> certOption = new(
    aliases: ["--cert"],
    description: "Client certificate to authenticate with")
{
    ArgumentHelpName = "file",
};

Option<string?> keyOption = new(
    aliases: ["--key"],
    description: "Private key file to authenticate with (or password for PFX certs)")
{
    ArgumentHelpName = "file",
};

Option<bool> resp3Option = new(
    aliases: ["-3"],
    description: "Start session in RESP3 protocol mode")
{
    Arity = ArgumentArity.Zero,
};

Option<bool> insecureOption = new(
    aliases: ["--insecure", "--trust"],
    description: "Allow insecure TLS connection by skipping cert validation")
{
    Arity = ArgumentArity.Zero,
};

Option<string?> sniOption = new(
    aliases: ["--sni"],
    description: "Server name indication for TLS")
{
    ArgumentHelpName = "host",
};

Option<int?> dbOption = new(
    aliases: ["-n"],
    description: "Database number")
{
    ArgumentHelpName = "db",
};

Option<bool> debugOption = new(
    aliases: ["--debug"],
    description: "Enable debug output")
{
    Arity = ArgumentArity.Zero,
    IsHidden = true,
};

Option<bool> flushOption = new(
    aliases: ["--flush"],
    description: "Enable auto flush")
{
    Arity = ArgumentArity.Zero,
    IsHidden = true,
};

Option<int> proxyPortOption = new(
    aliases: ["--proxyPort", "-pp"],
    description: "Local debugging proxy port")
{
    ArgumentHelpName = "port",
};
proxyPortOption.SetDefaultValue(6379);

Option<bool> runProxyServerOption = new(
    aliases: ["--proxy"],
    description: "Enable local debugging proxy")
{
    Arity = ArgumentArity.Zero,
};

Option<bool> debugProxyServerOption = new(
    aliases: ["--debugProxy"],
    description: "Self-connect to debugging proxy")
{
    Arity = ArgumentArity.Zero,
    IsHidden = true,
};

Option<int> repeatOption = new(
    aliases: ["-r"],
    description: "Execute specified command N times")
{
    ArgumentHelpName = "repeat",
};
repeatOption.SetDefaultValue(1);

Option<double> intervalOption = new(
    aliases: ["-i"],
    description: "When -r is used, waits <interval> seconds per command")
{
    ArgumentHelpName = "interval",
};
intervalOption.SetDefaultValue(0D);

repeatOption.SetDefaultValue(1);

Argument<string> cmdArg = new(
    name: "cmd", description: "RESP command to execute")
{
    Arity = ArgumentArity.ZeroOrOne,
};
Argument<string[]> argsArg = new(
    name: "arg", description: "RESP command argument(s)");

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
    sniOption,
    insecureOption,
    dbOption,
    debugOption,
    flushOption,
    proxyPortOption,
    runProxyServerOption,
    debugProxyServerOption,
    cmdArg,
    argsArg,
    repeatOption,
    intervalOption,
};

rootCommand.SetHandler(async ic =>
{
    var pass = passOption.Parse(ic);
    if (string.IsNullOrEmpty(pass))
    {
        pass = Environment.GetEnvironmentVariable("RESPCLI_AUTH");
    }
    var options = new ConnectionOptionsBag
    {
        Host = hostOption.Parse(ic),
        Port = portOption.Parse(ic),
        User = userOption.Parse(ic),
        Password = pass,
        Tls = tlsOption.Parse(ic),
        Resp3 = resp3Option.Parse(ic),
        CaCertPath = cacertOption.Parse(ic),
        UserCertPath = certOption.Parse(ic),
        UserKeyPathOrPassword = keyOption.Parse(ic),
        Sni = sniOption.Parse(ic),
        Log = ic.Console.WriteLine,
        TrustServerCert = insecureOption.Parse(ic),
        Database = dbOption.Parse(ic),
        DebugLog = debugOption.Parse(ic) ? ic.Console.WriteLine : null,
        AutoFlush = flushOption.Parse(ic),
        ProxyPort = proxyPortOption.Parse(ic),
        RunProxyServer = runProxyServerOption.Parse(ic),
        DebugProxyServer = debugProxyServerOption.Parse(ic),
        Repeat = repeatOption.Parse(ic),
        Interval = intervalOption.Parse(ic),
        Command = GetCommand(cmdArg.Parse(ic), argsArg.Parse(ic)),
    };
    options.Apply();

    try
    {
        if (guiOption.Parse(ic))
        {
            RespDesktop.Run(options);
        }
        else
        {
            using var conn = await Utils.ConnectAsync(options);
            if (conn is not null)
            {
                var handshake = Utils.GetHandshake(options);
                await RespClient.RunClient(conn, handshake, options.Command, options.Repeat, options.Interval, options.Database);
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

ImmutableArray<string> GetCommand(string cmd, string[] args)
{
    if (string.IsNullOrWhiteSpace(cmd))
    {
        return [];
    }
    cmd = cmd.Trim();
    if (args is null || args.Length == 0)
    {
        return [cmd];
    }
    return [cmd, .. args];
}

return await rootCommand.InvokeAsync(args);

internal sealed class ConnectionOptionsBag : ICloneable
{
    public string? UserKeyPathOrPassword { get; set; }
    public string? UserCertPath { get; set; }
    public string? CaCertPath { get; set; }
    public bool Resp3 { get; set; }
    public bool Tls { get; set; }
    public string? Password { get; set; }
    public string? User { get; set; }
    public int Port { get; set; } = 6379;
    public string Host { get; set; } = "127.0.0.1";
    public Action<string>? Log { get; set; }
    public bool Handshake { get; set; } = true;
    public string? Sni { get; set; }
    public bool TrustServerCert { get; set; }
    public int? Database { get; set; }
    public Action<string>? DebugLog { get; set; }
    public bool AutoFlush { get; set; }
    public int ProxyPort { get; set; } = 6379;
    public bool RunProxyServer { get; set; }
    public bool DebugProxyServer { get; set; }
    public int Repeat { get; set; } = 1;
    public double Interval { get; set; }
    public ImmutableArray<string> Command { get; set; } = ImmutableArray<string>.Empty;

    public void Apply()
    {
        if (!string.IsNullOrWhiteSpace(UserCertPath))
        {
            Tls = true; // user cert implies TLS
        }
    }

    public RemoteCertificateValidationCallback GetRemoteCertificateValidationCallback()
    {
        return (object _, X509Certificate? certificate, X509Chain? certificateChain, SslPolicyErrors sslPolicyError) =>
        {
            if (certificate is X509Certificate2 cert2)
            {
                Log?.Invoke($"Server certificate: {cert2.GetNameInfo(X509NameType.SimpleName, false)}");
                Log?.Invoke($"  thumbprint: {cert2.Thumbprint}");
                Log?.Invoke($"  expiration: {cert2.GetExpirationDateString()}");
            }

            // If we're already valid, there's nothing further to check
            if (sslPolicyError == SslPolicyErrors.None)
            {
                return true;
            }

            // If we're not valid due to chain errors - check against the trusted issuer
            // Note that we're only proceeding at all here if the *only* issue is chain errors (not more flags in SslPolicyErrors)
            if (sslPolicyError == SslPolicyErrors.RemoteCertificateChainErrors
                   && !string.IsNullOrWhiteSpace(CaCertPath)
                   && certificate is X509Certificate2 v2
#pragma warning disable SYSLIB0057 // Type or member is obsolete
                   && CheckTrustedIssuer(v2, certificateChain, new X509Certificate2(CaCertPath)))
#pragma warning restore SYSLIB0057 // Type or member is obsolete
            {
                return true;
            }

            if (TrustServerCert)
            {
                Log?.Invoke($"Trusting remote certificate despite policy failure: {sslPolicyError}");
                return true;
            }

            if ((sslPolicyError & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                var remote = (certificate as X509Certificate2)?.GetNameInfo(X509NameType.SimpleName, false);
                if (remote is not null)
                {
                    Log?.Invoke($"SNI mismatch; try --sni {remote}");
                }
            }

            Log?.Invoke($"Server certificate policy failure: {sslPolicyError}; use --trust to override");
            return false;
        };
    }

    private static readonly Oid _serverAuthOid = new Oid("1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.1");
    private static bool CheckTrustedIssuer(X509Certificate2 certificateToValidate, X509Chain? chainToValidate, X509Certificate2 authority)
    {
        // Reference:
        // https://stackoverflow.com/questions/6497040/how-do-i-validate-that-a-certificate-was-created-by-a-particular-certification-a
        // https://github.com/stewartadam/dotnet-x509-certificate-verification
        using X509Chain chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.VerificationTime = chainToValidate?.ChainPolicy?.VerificationTime ?? DateTime.Now;
        chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 0);
        // Ensure entended key usage checks are run and that we're observing a server TLS certificate
        chain.ChainPolicy.ApplicationPolicy.Add(_serverAuthOid);

        chain.ChainPolicy.ExtraStore.Add(authority);
        try
        {
            // This only verifies that the chain is valid, but with AllowUnknownCertificateAuthority could trust
            // self-signed or partial chained vertificates
            var chainIsVerified = chain.Build(certificateToValidate);
            if (chainIsVerified)
            {
                // Our method is "TrustIssuer", which means any intermediate cert we're being told to trust
                // is a valid thing to trust, up until it's a root CA
                bool found = false;
                byte[] authorityData = authority.RawData;
                foreach (var chainElement in chain.ChainElements)
                {
                    using var chainCert = chainElement.Certificate;
                    if (!found)
                    {
#if NET8_0_OR_GREATER
                        if (chainCert.RawDataMemory.Span.SequenceEqual(authorityData))
#else
                            if (chainCert.RawData.SequenceEqual(authorityData))
#endif
                        {
                            found = true;
                        }
                    }
                }
                return found;
            }
        }
        catch (CryptographicException)
        {
            // We specifically don't want to throw during validation here and would rather exit out gracefully
        }

        // If we didn't find the trusted issuer in the chain at all - we do not trust the result.
        return false;
    }

    internal LocalCertificateSelectionCallback? GetLocalCertificateSelectionCallback()
    {
        if (string.IsNullOrWhiteSpace(UserCertPath)) return null;

        string? key = string.IsNullOrWhiteSpace(UserKeyPathOrPassword) ? null : UserKeyPathOrPassword.Trim();
        X509Certificate2 pfx;
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        if (string.Equals(Path.GetExtension(UserCertPath), ".pfx", StringComparison.InvariantCultureIgnoreCase))
        {
            pfx = new X509Certificate2(UserCertPath, key);
        }
        else
        {
            // PEM handshakes not universally supported; prefer PFX
            using var pem = X509Certificate2.CreateFromPemFile(UserCertPath, key);
            pfx = new X509Certificate2(pem.Export(X509ContentType.Pfx));
        }
        bool logged = false;
#pragma warning restore SYSLIB0057 // Type or member is obsolete
        return (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
        {
            if (!logged)
            {
                logged = true; // can get called multiple times
                Log?.Invoke($"Client certificate: {pfx.GetNameInfo(X509NameType.SimpleName, false)}");
                Log?.Invoke($"  thumbprint: {pfx.Thumbprint}");
                Log?.Invoke($"  expiration: {pfx.GetExpirationDateString()}");
            }
            return pfx;
        };
    }

    object ICloneable.Clone() => Clone();
    internal ConnectionOptionsBag Clone() => new()
    {
        AutoFlush = AutoFlush,
        CaCertPath = CaCertPath,
        Database = Database,
        DebugLog = DebugLog,
        DebugProxyServer = DebugProxyServer,
        Handshake = Handshake,
        Host = Host,
        Log = Log,
        Password = Password,
        Port = Port,
        ProxyPort = ProxyPort,
        Resp3 = Resp3,
        RunProxyServer = RunProxyServer,
        Sni = Sni,
        Tls = Tls,
        TrustServerCert = TrustServerCert,
        User = User,
        UserCertPath = UserCertPath,
        UserKeyPathOrPassword = UserKeyPathOrPassword,
        Interval = Interval,
        Repeat = Repeat,
        Command = Command,
    };
}

internal static class OptionExtensions
{
    public static T Parse<T>(this Argument<T> argument, InvocationContext context)
    {
        var val = context.ParseResult.FindResultFor(argument)?.GetValueOrDefault();
        return val is null ? default! : (T)val;
    }

    public static T Parse<T>(this Option<T> option, InvocationContext context)
    {
        var val = context.ParseResult.FindResultFor(option)?.GetValueOrDefault();
        return val is null ? default! : (T)val;
    }
}
