using System.Net;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Provides context for an authentication request.</summary>
public sealed class TdsAuthenticationContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the user name sent by the client.</summary>
    public string? UserName { get; init; }

    /// <summary>Gets the password sent by the client.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the authentication token, if available.
    /// This value is extracted from token-oriented login payloads when possible.
    /// </summary>
    public string? AuthenticationToken { get; init; }

    /// <summary>Gets the requested initial database, if any.</summary>
    public string? Database { get; init; }

    /// <summary>Gets the application name sent by the client, if any.</summary>
    public string? ApplicationName { get; init; }
}
