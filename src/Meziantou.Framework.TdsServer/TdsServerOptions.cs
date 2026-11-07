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

    /// <summary>The default value of <see cref="MaxMessageSize"/>.</summary>
    public const int DefaultMaxMessageSize = 16 * 1024 * 1024;

    private readonly Lock _tlsCertificateLock = new();
    private int _packetSize = 4096;
    private int _maxMessageSize = DefaultMaxMessageSize;
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

    /// <summary>Gets or sets the maximum size, in bytes, of a single inbound TDS message.</summary>
    /// <remarks>
    /// A TDS message is made of as many packets as the client decides to send, and the server has to buffer the
    /// whole message before it can be parsed. This limit bounds that buffer. It applies from the very first
    /// packet of a connection, so it also bounds what an unauthenticated client can make the server allocate.
    /// Raise it when clients legitimately send large parameters or SQL batches. Must be at least
    /// <see cref="MaximumPacketSize"/> so that a single full packet always fits.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is smaller than <see cref="MaximumPacketSize"/>.</exception>
    public int MaxMessageSize
    {
        get => _maxMessageSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MaximumPacketSize);

            _maxMessageSize = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether encryption is required by the server.</summary>
    /// <remarks>
    /// When this is <see langword="false"/> and no TLS certificate is configured, the server answers PRELOGIN
    /// with NOT_SUPPORTED, and clients that allow it (for example <c>Encrypt=Optional</c>) log in over an
    /// unencrypted connection. The LOGIN7 password is then protected only by the TDS nibble-swap and XOR
    /// obfuscation, which is trivially reversible by anyone on the network path. Configure
    /// <see cref="TlsPfxPath"/> or the PEM options for any deployment that is not loopback-only.
    /// </remarks>
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
