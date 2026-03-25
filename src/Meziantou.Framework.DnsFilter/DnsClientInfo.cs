using System.Net;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Provides information about the DNS client making the query.
/// Used for evaluating <c>$client</c> and <c>$ctag</c> modifiers.
/// All properties are optional; when not set, rules with corresponding modifiers will not match.
/// </summary>
public readonly struct DnsClientInfo
{
    /// <summary>
    /// Gets the IP address of the client. Used for <c>$client</c> modifier matching by IP or CIDR.
    /// </summary>
    public IPAddress? Address { get; init; }

    /// <summary>
    /// Gets the client name. Used for <c>$client</c> modifier matching by name.
    /// The caller is responsible for resolving client IP to name before calling the filter engine.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the client tags. Used for <c>$ctag</c> modifier matching.
    /// Tags are arbitrary strings (e.g., <c>device_phone</c>, <c>os_windows</c>, <c>user_child</c>).
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }
}
