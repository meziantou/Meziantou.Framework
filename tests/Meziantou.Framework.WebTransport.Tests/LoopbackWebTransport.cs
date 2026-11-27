using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.MsQuic;

namespace Meziantou.Framework.WebTransport.Tests;

// A WebTransport listener on the loopback interface, with a certificate the clients trust by its hash
internal sealed class LoopbackWebTransport : IAsyncDisposable
{
    private LoopbackWebTransport(WebTransportListener listener, X509Certificate2 certificate)
    {
        Listener = listener;
        Certificate = certificate;
    }

    public WebTransportListener Listener { get; }

    public X509Certificate2 Certificate { get; }

    public static async Task<LoopbackWebTransport> StartAsync()
    {
        global::Xunit.Assert.SkipUnless(MsQuicLibrary.IsSupported, "MsQuic is not available: " + MsQuicLibrary.NotSupportedReason);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using var ephemeralCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(10));
        var certificate = X509CertificateLoader.LoadPkcs12(ephemeralCertificate.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
        var listener = await WebTransportListener.ListenAsync(new WebTransportListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ServerCertificate = certificate,
        });

        return new LoopbackWebTransport(listener, certificate);
    }

    public Uri GetUri(string path) => new($"https://localhost:{Listener.LocalEndPoint.Port}{path}");

    public WebTransportClientOptions CreateClientOptions()
    {
        var options = new WebTransportClientOptions();
        options.ServerCertificateHashes.Add(SHA256.HashData(Certificate.RawData));
        return options;
    }

    public async Task<(WebTransportSession Client, WebTransportSession Server)> ConnectAsync()
    {
        var connectTask = WebTransportSession.ConnectAsync(GetUri("/"), CreateClientOptions()).AsTask();
        var request = await Listener.AcceptAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var server = await request.AcceptAsync();
        var client = await connectTask.WaitAsync(TimeSpan.FromSeconds(10));
        return (client, server);
    }

    public async ValueTask DisposeAsync()
    {
        await Listener.DisposeAsync();
        Certificate.Dispose();
    }
}
