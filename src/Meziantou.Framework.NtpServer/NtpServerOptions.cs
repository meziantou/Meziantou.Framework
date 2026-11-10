using System.Net;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Configuration options for <see cref="NtpServer"/>.
/// </summary>
public sealed class NtpServerOptions
{
    /// <summary>Gets or sets the port to listen on. Default is 0 (auto-assign).</summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the address to bind to. Default is <see cref="IPAddress.Any"/>, which serves every
    /// network interface. Use <see cref="IPAddress.Loopback"/> to serve only the local machine.
    /// </summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Any;

    /// <summary>Gets or sets the time provider used to answer queries. Default is <see cref="System.TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the stratum level to advertise. Default is 2.
    /// </summary>
    /// <remarks>
    /// Stratum 1 claims a directly attached reference clock such as GPS. A server answering from
    /// <see cref="TimeProvider"/> has no such clock, so the honest value is 2 or higher; set
    /// <see cref="ReferenceIdentifier"/> to match if you do attach one.
    /// </remarks>
    public byte Stratum { get; set; } = 2;

    /// <summary>
    /// Gets or sets the reference identifier, up to four ASCII characters. Default is <c>LOCL</c>,
    /// the conventional identifier for an undisciplined local clock.
    /// </summary>
    public string ReferenceIdentifier { get; set; } = "LOCL";

    /// <summary>
    /// Gets or sets the maximum error the server advertises for its own clock (the root dispersion).
    /// Default is 100 milliseconds.
    /// </summary>
    /// <remarks>
    /// Clients use this as the error bound on the offset they compute. Zero claims a perfectly accurate
    /// clock, which is never true, so the default is a deliberately conservative bound for an
    /// undisciplined software clock. Lower it if the underlying <see cref="TimeProvider"/> is
    /// disciplined and you know its accuracy.
    /// </remarks>
    public TimeSpan RootDispersion { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum number of requests answered per second per source address.
    /// Default is 100. Set to 0 to disable rate limiting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The limit is approximate: source addresses are mapped onto a fixed number of buckets so that
    /// memory cannot grow with the number of distinct (and easily spoofed) source addresses seen, and
    /// colliding addresses share a budget.
    /// </para>
    /// <para>
    /// Rate limiting is measured against the real system clock rather than <see cref="TimeProvider"/>,
    /// so that a simulated or frozen clock cannot disable it or wedge it permanently.
    /// </para>
    /// </remarks>
    public int MaxRequestsPerSecond { get; set; } = 100;

    /// <summary>
    /// Gets the symmetric keys the server accepts to authenticate requests (RFC 5905, RFC 8573). Default is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that ends with a valid message authentication code (MAC) computed with one of these keys gets a reply
    /// authenticated with the same key. A request whose MAC uses an unknown key identifier, or is not valid, gets a
    /// crypto-NAK: a reply with no time, that tells the client that the server could not authenticate it.
    /// </para>
    /// <para>
    /// The key identifiers must be unique. A request without a MAC gets an unauthenticated reply, unless
    /// <see cref="RequireAuthentication"/> is <see langword="true"/>. The server reads the keys when it is created:
    /// later changes to this collection do not apply to it.
    /// </para>
    /// </remarks>
    public ICollection<NtpAuthenticationKey> AuthenticationKeys { get; } = new List<NtpAuthenticationKey>();

    /// <summary>
    /// Gets or sets a value indicating whether the server ignores the requests that are not authenticated with one of
    /// <see cref="AuthenticationKeys"/>. Default is <see langword="false"/>.
    /// </summary>
    public bool RequireAuthentication { get; set; }
}
