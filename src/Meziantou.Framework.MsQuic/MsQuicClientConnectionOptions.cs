using System.Net;
using System.Net.Security;

namespace Meziantou.Framework.MsQuic;

/// <summary>The options of a client connection, see <see cref="MsQuicConnection.ConnectAsync"/>.</summary>
public sealed class MsQuicClientConnectionOptions : MsQuicConnectionOptions
{
    /// <summary>Gets or sets the address of the server: an <see cref="IPEndPoint"/>, or a <see cref="DnsEndPoint"/> whose host name is resolved.</summary>
    public EndPoint? RemoteEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the TLS options. <see cref="SslClientAuthenticationOptions.ApplicationProtocols"/> is required;
    /// <see cref="SslClientAuthenticationOptions.TargetHost"/> defaults to the host of <see cref="RemoteEndPoint"/>. The
    /// server certificate is validated like <see cref="SslStream"/> does, unless
    /// <see cref="SslClientAuthenticationOptions.RemoteCertificateValidationCallback"/> decides. Client certificates are
    /// not supported.
    /// </summary>
    public SslClientAuthenticationOptions ClientAuthenticationOptions { get; set; } = new();

    internal static void Validate(MsQuicClientConnectionOptions options)
    {
        MsQuicConnectionOptions.Validate(options);
        if (options.RemoteEndPoint is not IPEndPoint and not DnsEndPoint)
            throw new ArgumentException($"{nameof(RemoteEndPoint)} must be an {nameof(IPEndPoint)} or a {nameof(DnsEndPoint)}.", nameof(options));

        if (options.ClientAuthenticationOptions?.ApplicationProtocols is not { Count: > 0 })
            throw new ArgumentException($"{nameof(ClientAuthenticationOptions)}.{nameof(SslClientAuthenticationOptions.ApplicationProtocols)} must contain at least one protocol.", nameof(options));
    }
}
