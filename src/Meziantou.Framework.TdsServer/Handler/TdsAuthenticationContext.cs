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

    /// <summary>
    /// Gets a value indicating whether the client asked for integrated security (<c>Integrated Security=true</c>),
    /// in which case it sends an SSPI token instead of a SQL login.
    /// </summary>
    public bool IsIntegratedSecurity { get; init; }

    /// <summary>Gets the SSPI token sent by the client in the current round of the SSPI exchange, or an empty value when it sent none.</summary>
    /// <remarks>
    /// The first round carries the token of the LOGIN7 packet. When the handler returns
    /// <see cref="TdsAuthenticationResult.ContinueSspi"/>, the server sends the handler's token to the client and calls the
    /// handler again, with the same context, once the client answers: this property then holds the client's answer.
    /// </remarks>
    public ReadOnlyMemory<byte> SspiToken { get; internal set; }

    /// <summary>Gets a dictionary that keeps state across the rounds of an SSPI exchange, such as a <c>NegotiateAuthentication</c> instance.</summary>
    /// <remarks>The server disposes the <see cref="IDisposable"/> values of the dictionary once the login completes or fails.</remarks>
    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
}
