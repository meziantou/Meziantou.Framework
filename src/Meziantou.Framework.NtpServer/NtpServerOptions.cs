namespace Meziantou.Framework.NtpServer;

/// <summary>
/// Configuration options for <see cref="NtpServer"/>.
/// </summary>
public sealed class NtpServerOptions
{
    /// <summary>Gets or sets the port to listen on. Default is 0 (auto-assign).</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the time provider used by the server. Default is <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Gets or sets the stratum level to advertise. Default is 1 (primary reference).</summary>
    public byte Stratum { get; set; } = 1;
}
