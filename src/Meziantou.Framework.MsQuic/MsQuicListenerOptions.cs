using System.Net;
using System.Net.Security;

namespace Meziantou.Framework.MsQuic;

/// <summary>The options of a <see cref="MsQuicListener"/>.</summary>
public sealed class MsQuicListenerOptions
{
    /// <summary>Gets or sets the address to listen on. Port 0 picks a free port; <see cref="IPAddress.IPv6Any"/> listens on IPv4 and IPv6.</summary>
    public IPEndPoint ListenEndPoint { get; set; } = new(IPAddress.IPv6Any, 0);

    /// <summary>Gets the application protocols (ALPN) the listener accepts.</summary>
    public IList<SslApplicationProtocol> ApplicationProtocols { get; } = [];

    /// <summary>Gets or sets the number of connections that can wait for <see cref="MsQuicListener.AcceptConnectionAsync"/>. The default is 512.</summary>
    public int ListenBacklog { get; set; } = 512;

    /// <summary>
    /// Gets or sets the function that returns the options of a new connection, from the TLS client hello (server name).
    /// The configuration of each returned instance is cached: return the same instance for the same options.
    /// </summary>
    public Func<SslClientHelloInfo, CancellationToken, ValueTask<MsQuicServerConnectionOptions>>? ConnectionOptionsCallback { get; set; }

    internal static void Validate(MsQuicListenerOptions options)
    {
        if (options.ListenEndPoint is null)
            throw new ArgumentException($"{nameof(ListenEndPoint)} is required.", nameof(options));

        if (options.ApplicationProtocols.Count == 0)
            throw new ArgumentException($"{nameof(ApplicationProtocols)} must contain at least one protocol.", nameof(options));

        if (options.ConnectionOptionsCallback is null)
            throw new ArgumentException($"{nameof(ConnectionOptionsCallback)} is required.", nameof(options));

        if (options.ListenBacklog < 1)
            throw new ArgumentException($"{nameof(ListenBacklog)} must be at least 1.", nameof(options));
    }
}
