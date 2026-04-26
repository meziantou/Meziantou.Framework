using System.Net;

namespace Meziantou.Framework.Tds;

/// <summary>Describes a TCP endpoint used by the TDS server.</summary>
public sealed class TdsTcpListenerOptions
{
    /// <summary>Gets or sets the address to bind to.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the port to listen on.</summary>
    public int Port { get; set; } = 1433;
}
