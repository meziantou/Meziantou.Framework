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
    /// security decision. Disabling it does not disable <see cref="AuthenticationKey"/>.
    /// </para>
    /// </remarks>
    public bool ValidateResponse { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of times a request is sent again to the same address when no valid reply arrives within
    /// <see cref="Timeout"/>. Default is 0.
    /// </summary>
    /// <remarks>
    /// Each attempt sends a new request and waits <see cref="Timeout"/> for its reply, so an address can take
    /// <c>Timeout × (RetryCount + 1)</c>. Only a missing reply is retried, not a network error.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int RetryCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the symmetric key that authenticates the requests and the replies (RFC 5905, RFC 8573), or
    /// <see langword="null"/> to send unauthenticated requests. Default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When set, the client appends a message authentication code (MAC) to every request, and ignores every reply that
    /// does not end with a valid MAC computed with the same key, whatever <see cref="ValidateResponse"/> is. The server
    /// must be configured with the same key identifier, algorithm and key.
    /// </remarks>
    public NtpAuthenticationKey? AuthenticationKey { get; set; }
}
