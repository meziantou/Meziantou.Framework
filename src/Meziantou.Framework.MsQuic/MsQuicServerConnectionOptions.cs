using System.Net.Security;

namespace Meziantou.Framework.MsQuic;

/// <summary>The options of a connection a <see cref="MsQuicListener"/> accepts.</summary>
public sealed class MsQuicServerConnectionOptions : MsQuicConnectionOptions
{
    /// <summary>
    /// Gets or sets the TLS options. <see cref="SslServerAuthenticationOptions.ServerCertificate"/> or
    /// <see cref="SslServerAuthenticationOptions.ServerCertificateContext"/> is required. Client certificates are not
    /// supported.
    /// </summary>
    public SslServerAuthenticationOptions ServerAuthenticationOptions { get; set; } = new();

    internal static void Validate(MsQuicServerConnectionOptions options)
    {
        MsQuicConnectionOptions.Validate(options);
        if (options.ServerAuthenticationOptions is null || (options.ServerAuthenticationOptions.ServerCertificate is null && options.ServerAuthenticationOptions.ServerCertificateContext is null))
            throw new ArgumentException($"{nameof(ServerAuthenticationOptions)} must set a server certificate.", nameof(options));
    }
}
