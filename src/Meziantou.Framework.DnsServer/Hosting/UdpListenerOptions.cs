using System.Net;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Configuration for a UDP DNS listener.</summary>
public sealed class UdpListenerOptions
{
    /// <summary>Gets or sets the port to listen on.</summary>
    public int Port { get; set; } = 53;

    /// <summary>Gets or sets the address to bind to.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Any;
}
