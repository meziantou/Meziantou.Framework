using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Tds;

/// <summary>Configuration options for the TDS server.</summary>
public sealed class TdsServerOptions
{
    private readonly Lock _tlsCertificateLock = new();
    private X509Certificate2? _tlsCertificate;
    private bool _tlsCertificateLoaded;

    internal List<TdsTcpListenerOptions> TcpListeners { get; } = [];

    /// <summary>Gets or sets the packet size used when writing TDS packets.</summary>
    public int PacketSize { get; set; } = 4096;

    /// <summary>Gets or sets a value indicating whether encryption is required by the server.</summary>
    public bool RequireEncryption { get; set; }

    /// <summary>Gets or sets the path to a PFX certificate file used for TLS.</summary>
    public string? TlsPfxPath { get; set; }

    /// <summary>Gets or sets the password used to open the PFX certificate file.</summary>
    public string? TlsPfxPassword { get; set; }

    /// <summary>Gets or sets the path to a PEM certificate file used for TLS.</summary>
    public string? TlsPemCertificatePath { get; set; }

    /// <summary>Gets or sets the path to a PEM private key file used for TLS.</summary>
    public string? TlsPemPrivateKeyPath { get; set; }

    /// <summary>Adds a TCP listener.</summary>
    /// <param name="port">TCP port to listen on.</param>
    /// <param name="bindAddress">Address to bind to. Defaults to loopback.</param>
    /// <returns>The current options instance.</returns>
    public TdsServerOptions AddTcpListener(int port = 1433, IPAddress? bindAddress = null)
    {
        TcpListeners.Add(new TdsTcpListenerOptions
        {
            Port = port,
            BindAddress = bindAddress ?? IPAddress.Loopback,
        });

        return this;
    }

    internal X509Certificate2? GetTlsCertificate()
    {
        if (_tlsCertificateLoaded)
        {
            return _tlsCertificate;
        }

        lock (_tlsCertificateLock)
        {
            if (_tlsCertificateLoaded)
            {
                return _tlsCertificate;
            }

            _tlsCertificate = TdsServerCertificateLoader.Load(this);
            _tlsCertificateLoaded = true;
            return _tlsCertificate;
        }
    }
}
