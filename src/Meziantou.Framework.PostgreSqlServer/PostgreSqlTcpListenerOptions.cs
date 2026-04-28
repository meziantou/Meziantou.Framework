using System.Net;

namespace Meziantou.Framework.PostgreSql;

/// <summary>Describes a TCP endpoint used by the PostgreSQL server.</summary>
public sealed class PostgreSqlTcpListenerOptions
{
    /// <summary>Gets or sets the address to bind to.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the port to listen on.</summary>
    public int Port { get; set; } = 5432;
}
