using System.Net;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Configuration options for <see cref="NtpClient"/>.
/// </summary>
public sealed class NtpClientOptions
{
    internal static NtpClientOptions Default { get; } = new();

    /// <summary>Gets or sets the server port. Default is 123.</summary>
    public int Port { get; set; } = 123;

    /// <summary>Gets or sets the NTP protocol version to use. Default is <see cref="NtpVersion.V4"/>.</summary>
    public NtpVersion Version { get; set; } = NtpVersion.V4;
}
