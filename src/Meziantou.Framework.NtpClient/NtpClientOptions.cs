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

    /// <summary>Gets or sets the timeout for querying a single resolved address. Default is 5 seconds. Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for no timeout.</summary>
    /// <remarks>
    /// A host name can resolve to several addresses, and each one gets this much time, so a query can
    /// take up to <c>Timeout × addressCount</c> overall. Use the <c>cancellationToken</c> of
    /// <see cref="NtpClient.QueryAsync"/> to bound the total duration.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a value indicating whether responses are validated before being returned.
    /// Default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, a response is only accepted if its originate timestamp echoes the transmit
    /// timestamp of the request (RFC 5905 TEST2), its version is 3 or 4, its stratum is not 0
    /// (Kiss-o'-Death), and the server does not report an alarm condition. Responses that fail are
    /// ignored and the client keeps waiting for a valid one until <see cref="Timeout"/> elapses.
    /// </para>
    /// <para>
    /// The originate timestamp is what ties a reply to this specific request, so disabling this makes
    /// the client accept a forged reply from anyone who can guess its source port. Only turn it off to
    /// interoperate with a server known to be non-compliant, and do not use the result to make a
    /// security decision. This library never authenticates the server cryptographically: it implements
    /// neither NTS (RFC 8915) nor symmetric key authentication.
    /// </para>
    /// </remarks>
    public bool ValidateResponse { get; set; } = true;
}
