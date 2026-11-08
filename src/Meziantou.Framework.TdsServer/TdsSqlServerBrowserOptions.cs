using System.Net;

namespace Meziantou.Framework.Tds;

/// <summary>Configures the SQL Server Browser responder, which lets clients connect with an instance name (<c>host\INSTANCE</c>).</summary>
/// <remarks>
/// The responder answers the SQL Server Resolution Protocol (MC-SQLR) on a UDP port. Clients send it the instance
/// name and receive the TCP port of the server.
/// </remarks>
public sealed class TdsSqlServerBrowserOptions
{
    /// <summary>The UDP port of the SQL Server Browser service. Clients always send their requests to this port.</summary>
    public const int DefaultPort = 1434;

    /// <summary>The instance name of a default SQL Server instance.</summary>
    public const string DefaultInstanceName = "MSSQLSERVER";

    /// <summary>Gets or sets the instance name the responder advertises.</summary>
    /// <remarks>Clients compare it without case sensitivity. It cannot contain a semicolon.</remarks>
    public string InstanceName { get; set; } = DefaultInstanceName;

    /// <summary>Gets or sets the server name the responder advertises, or <see langword="null"/> to use <see cref="Environment.MachineName"/>.</summary>
    public string? ServerName { get; set; }

    /// <summary>Gets or sets the address the UDP socket binds to.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the UDP port to listen on.</summary>
    /// <remarks>Clients only query <see cref="DefaultPort"/>. Use 0 to bind a free port, for example in tests that send the requests themselves.</remarks>
    public int Port { get; set; } = DefaultPort;
}
