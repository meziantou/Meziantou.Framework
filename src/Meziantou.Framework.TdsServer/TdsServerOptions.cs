using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Tds;

/// <summary>Configuration options for the TDS server.</summary>
public sealed class TdsServerOptions
{
    /// <summary>The smallest packet size a TDS client is required to support.</summary>
    public const int MinimumPacketSize = 512;

    /// <summary>The largest packet size a TDS packet header can describe, since it stores the length in 16 bits.</summary>
    public const int MaximumPacketSize = ushort.MaxValue;

    private readonly Lock _tlsCertificateLock = new();
    private int _packetSize = 4096;
    private X509Certificate2? _tlsCertificate;
    private bool _tlsCertificateLoaded;

    internal List<TdsTcpListenerOptions> TcpListeners { get; } = [];

    /// <summary>Gets or sets the packet size used when writing TDS packets.</summary>
    /// <remarks>
    /// Must be between <see cref="MinimumPacketSize"/> and <see cref="MaximumPacketSize"/>. The TDS packet
    /// header stores the packet length in 16 bits, so a larger value cannot be represented on the wire.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the supported range.</exception>
    public int PacketSize
    {
        get => _packetSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumPacketSize);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumPacketSize);

            _packetSize = value;
        }
    }

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
