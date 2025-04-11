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
        UserKeyPath = keyOption.Parse(ic),
        Log = ic.Console.WriteLine,
    };
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

internal sealed class ConnectionOptionsBag
{
    public string? UserKeyPath { get; set; }
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

    public RemoteCertificateValidationCallback GetRemoteCertificateValidationCallback()
    {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return string.IsNullOrWhiteSpace(CaCertPath)
            ? TrustAnyServer() : TrustIssuerCallback(new X509Certificate2(CaCertPath));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
    }

    private static RemoteCertificateValidationCallback TrustIssuerCallback(X509Certificate2 issuer)
    {
        if (issuer == null) throw new ArgumentNullException(nameof(issuer));

        return (object _, X509Certificate? certificate, X509Chain? certificateChain, SslPolicyErrors sslPolicyError) =>
        {
            // If we're already valid, there's nothing further to check
            if (sslPolicyError == SslPolicyErrors.None)
            {
                return true;
            }
            // If we're not valid due to chain errors - check against the trusted issuer
            // Note that we're only proceeding at all here if the *only* issue is chain errors (not more flags in SslPolicyErrors)
            return sslPolicyError == SslPolicyErrors.RemoteCertificateChainErrors
                   && certificate is X509Certificate2 v2
                   && CheckTrustedIssuer(v2, certificateChain, issuer);
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
    private RemoteCertificateValidationCallback TrustAnyServer()
        => (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
        {
            if (certificate is X509Certificate2 cert2)
            {
                Log?.Invoke($"Server certificate: {certificate.Subject} ({cert2.Thumbprint})");
            }
            else
            {
                Log?.Invoke($"Server certificate: {certificate?.Subject}");
            }
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                Log?.Invoke($"Ignoring certificate policy failure (ignoring): {sslPolicyErrors}");
            }
            return true;
        };
    internal LocalCertificateSelectionCallback? GetLocalCertificateSelectionCallback()
    {
        if (string.IsNullOrWhiteSpace(UserCertPath)) return null;

        // PEM handshakes not universally supported; prefer PFX
        using var pem = X509Certificate2.CreateFromPemFile(UserCertPath, UserKeyPath);
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        var pfx = new X509Certificate2(pem.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
        return (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) => pfx;
    }
}

internal static class OptionExtensions
{
    public static T Parse<T>(this Option<T> option, InvocationContext context)
    {
        var val = context.ParseResult.FindResultFor(option)?.GetValueOrDefault();
        return val is null ? default! : (T)val;
    }
}
