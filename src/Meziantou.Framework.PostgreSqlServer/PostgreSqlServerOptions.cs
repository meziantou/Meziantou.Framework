using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql;

/// <summary>Configuration options for the PostgreSQL server.</summary>
public sealed class PostgreSqlServerOptions
{
    /// <summary>The default value of <see cref="MaxMessageSize"/>.</summary>
    public const int DefaultMaxMessageSize = 16 * 1024 * 1024;

    /// <summary>The maximum size, in bytes, of a startup packet. Matches PostgreSQL's own limit.</summary>
    public const int MaxStartupPacketSize = 10000;

    private readonly Lock _tlsCertificateLock = new();
    private readonly ConcurrentDictionary<(int ProcessId, int SecretKey), PostgreSqlBackendSession> _backendSessions = new();
    private X509Certificate2? _tlsCertificate;
    private volatile bool _tlsCertificateLoaded;
    private int _maxMessageSize = DefaultMaxMessageSize;
    private int _maxConcurrentConnections = 1000;
    private int _maxPreparedStatementsPerConnection = 1000;
    private int _maxPortalsPerConnection = 1000;
    private TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(30);
    private TimeSpan _idleTimeout = TimeSpan.FromMinutes(30);

    internal List<PostgreSqlTcpListenerOptions> TcpListeners { get; } = [];

    /// <summary>Gets or sets the maximum size, in bytes, of a single inbound message.</summary>
    /// <remarks>
    /// The server buffers a whole message before parsing it, and the length is declared by the client. This limit
    /// bounds that buffer. It applies from the very first message of a connection, so it also bounds what an
    /// unauthenticated client can make the server allocate. Raise it when clients legitimately send large
    /// parameters or SQL batches. The startup packet is bounded separately by <see cref="MaxStartupPacketSize"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not strictly positive.</exception>
    public int MaxMessageSize
    {
        get => _maxMessageSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _maxMessageSize = value;
        }
    }

    /// <summary>Gets or sets the maximum number of connections served concurrently. Additional connections are closed immediately.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not strictly positive.</exception>
    public int MaxConcurrentConnections
    {
        get => _maxConcurrentConnections;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _maxConcurrentConnections = value;
        }
    }

    /// <summary>Gets or sets the maximum number of prepared statements a single connection may hold.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not strictly positive.</exception>
    public int MaxPreparedStatementsPerConnection
    {
        get => _maxPreparedStatementsPerConnection;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _maxPreparedStatementsPerConnection = value;
        }
    }

    /// <summary>Gets or sets the maximum number of portals a single connection may hold.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not strictly positive.</exception>
    public int MaxPortalsPerConnection
    {
        get => _maxPortalsPerConnection;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            _maxPortalsPerConnection = value;
        }
    }

    /// <summary>Gets or sets how long an unauthenticated connection may take to complete TLS negotiation and authentication.</summary>
    /// <remarks>Use <see cref="Timeout.InfiniteTimeSpan"/> to disable. Leaving it disabled lets an unauthenticated peer hold a connection open indefinitely.</remarks>
    public TimeSpan HandshakeTimeout
    {
        get => _handshakeTimeout;
        set
        {
            if (value != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            }

            _handshakeTimeout = value;
        }
    }

    /// <summary>Gets or sets how long an authenticated connection may stay idle between messages before it is closed.</summary>
    /// <remarks>Use <see cref="Timeout.InfiniteTimeSpan"/> to disable.</remarks>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set
        {
            if (value != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            }

            _idleTimeout = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether encryption is required by the server.</summary>
    public bool RequireEncryption { get; set; }

    /// <summary>Gets or sets the authentication method requested from clients.</summary>
    public PostgreSqlAuthenticationMethod AuthenticationMethod { get; set; } = PostgreSqlAuthenticationMethod.ScramSha256;

    /// <summary>Gets or sets the PostgreSQL server version reported to clients.</summary>
    public string ServerVersion { get; set; } = "16.0";

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
    public PostgreSqlServerOptions AddTcpListener(int port = 5432, IPAddress? bindAddress = null)
    {
        TcpListeners.Add(new PostgreSqlTcpListenerOptions
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

            var certificate = PostgreSqlServerCertificateLoader.Load(this);
            _tlsCertificate = certificate;

            // Written last: _tlsCertificateLoaded is volatile, so a reader that observes true also observes the certificate.
            _tlsCertificateLoaded = true;
            return certificate;
        }
    }

    internal (int ProcessId, int SecretKey, PostgreSqlBackendSession Session) RegisterBackendSession()
    {
        while (true)
        {
            var processId = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            var secretKey = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            var session = new PostgreSqlBackendSession();
            if (_backendSessions.TryAdd((processId, secretKey), session))
            {
                return (processId, secretKey, session);
            }
        }
    }

    internal bool TryCancelBackendSession(int processId, int secretKey)
    {
        if (_backendSessions.TryGetValue((processId, secretKey), out var session))
        {
            session.CancelCurrentCommand();
            return true;
        }

        return false;
    }

    internal void UnregisterBackendSession(int processId, int secretKey)
    {
        _ = _backendSessions.TryRemove((processId, secretKey), out _);
    }
}
