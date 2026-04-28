using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.PostgreSql.Handler;

namespace Meziantou.Framework.PostgreSql;

/// <summary>Configuration options for the PostgreSQL server.</summary>
public sealed class PostgreSqlServerOptions
{
    private readonly Lock _tlsCertificateLock = new();
    private readonly ConcurrentDictionary<(int ProcessId, int SecretKey), PostgreSqlBackendSession> _backendSessions = new();
    private X509Certificate2? _tlsCertificate;
    private bool _tlsCertificateLoaded;

    internal List<PostgreSqlTcpListenerOptions> TcpListeners { get; } = [];

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

            _tlsCertificate = PostgreSqlServerCertificateLoader.Load(this);
            _tlsCertificateLoaded = true;
            return _tlsCertificate;
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
