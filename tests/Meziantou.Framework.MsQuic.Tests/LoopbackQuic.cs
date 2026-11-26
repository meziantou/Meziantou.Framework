using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.MsQuic.Tests;

// A listener on the loopback interface with a self-signed certificate, and the options of a client that trusts it.
internal sealed class LoopbackQuic : IAsyncDisposable
{
    public static readonly SslApplicationProtocol Protocol = new("test");

    private LoopbackQuic(MsQuicListener listener, X509Certificate2 certificate)
    {
        Listener = listener;
        Certificate = certificate;
    }

    public MsQuicListener Listener { get; }

    public X509Certificate2 Certificate { get; }

    public static async Task<LoopbackQuic> StartAsync(Action<MsQuicServerConnectionOptions>? configureServer = null)
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        var certificate = CreateCertificate();
        var serverOptions = new MsQuicServerConnectionOptions
        {
            EnableDatagrams = true,
            IdleTimeout = TimeSpan.FromSeconds(30),
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ApplicationProtocols = [Protocol],
            },
        };
        configureServer?.Invoke(serverOptions);

        var listenerOptions = new MsQuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ConnectionOptionsCallback = (_, _) => ValueTask.FromResult(serverOptions),
        };
        listenerOptions.ApplicationProtocols.Add(Protocol);
        var listener = await MsQuicListener.ListenAsync(listenerOptions);
        return new LoopbackQuic(listener, certificate);
    }

    public MsQuicClientConnectionOptions CreateClientOptions(bool enableDatagrams = true) => new()
    {
        RemoteEndPoint = Listener.LocalEndPoint,
        EnableDatagrams = enableDatagrams,
        IdleTimeout = TimeSpan.FromSeconds(30),
        ClientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            ApplicationProtocols = [Protocol],
            TargetHost = "localhost",
            RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && certificate.GetCertHash(HashAlgorithmName.SHA256).AsSpan().SequenceEqual(Certificate.GetCertHash(HashAlgorithmName.SHA256)),
        },
    };

    // Connects a client and accepts the connection on the server
    public async Task<(MsQuicConnection Client, MsQuicConnection Server)> ConnectAsync(MsQuicClientConnectionOptions? options = null)
    {
        var acceptTask = Listener.AcceptConnectionAsync().AsTask();
        var client = await MsQuicConnection.ConnectAsync(options ?? CreateClientOptions());
        var server = await acceptTask.WaitAsync(TimeSpan.FromSeconds(30));
        return (client, server);
    }

    public async ValueTask DisposeAsync()
    {
        await Listener.DisposeAsync();
        Certificate.Dispose();
    }

    // ECDSA P-256, valid 10 days: the certificates WebTransport accepts in serverCertificateHashes. Schannel needs a
    // key it can open, so the certificate is reloaded from a PKCS #12 export; with OpenSSL, MsQuic exports the key.
    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(10));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
    }
}
