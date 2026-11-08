using System.Security.Claims;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents the result of an authentication attempt.</summary>
public sealed class TdsAuthenticationResult
{
    private TdsAuthenticationResult()
    {
    }

    /// <summary>Gets a value indicating whether authentication succeeded.</summary>
    public bool IsAuthenticated { get; private init; }

    /// <summary>Gets the error message to send to the client on failure.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Gets the SQL Server-style error number to send on failure.</summary>
    public uint ErrorNumber { get; private init; } = 18456;

    /// <summary>Gets the error state to send on failure.</summary>
    public byte ErrorState { get; private init; } = 1;

    /// <summary>Gets the error class to send on failure.</summary>
    public byte ErrorClass { get; private init; } = 14;

    /// <summary>Gets the default database to report after login, if any.</summary>
    public string? Database { get; private init; }

    /// <summary>Gets the authenticated user context that will be propagated to query handlers.</summary>
    public ClaimsPrincipal? UserContext { get; private init; }

    /// <summary>Gets a value indicating whether the SSPI exchange goes on: the server sends <see cref="SspiToken"/> to the client and waits for its answer.</summary>
    public bool IsSspiContinuationNeeded { get; private init; }

    /// <summary>Gets the SSPI token to send to the client, or an empty value when there is none.</summary>
    public ReadOnlyMemory<byte> SspiToken { get; private init; }

    /// <summary>Creates a successful authentication result.</summary>
    public static TdsAuthenticationResult Success(string? database = null, ClaimsPrincipal? userContext = null)
    {
        return new TdsAuthenticationResult
        {
            IsAuthenticated = true,
            Database = database,
            UserContext = userContext,
        };
    }

    /// <summary>Creates a successful authentication result that ends an SSPI exchange with a last token for the client.</summary>
    /// <param name="sspiToken">The final SSPI token, sent to the client before the login acknowledgment. It can be empty.</param>
    /// <param name="database">The default database to report after login, if any.</param>
    /// <param name="userContext">The authenticated user context that will be propagated to query handlers.</param>
    public static TdsAuthenticationResult Success(ReadOnlyMemory<byte> sspiToken, string? database = null, ClaimsPrincipal? userContext = null)
    {
        return new TdsAuthenticationResult
        {
            IsAuthenticated = true,
            Database = database,
            UserContext = userContext,
            SspiToken = sspiToken,
        };
    }

    /// <summary>Creates a result that continues the SSPI exchange: the server sends the token to the client and calls the handler again with the client's answer.</summary>
    /// <param name="sspiToken">The SSPI token to send to the client.</param>
    public static TdsAuthenticationResult ContinueSspi(ReadOnlyMemory<byte> sspiToken)
    {
        if (sspiToken.IsEmpty)
        {
            throw new ArgumentException("The SSPI token cannot be empty.", nameof(sspiToken));
        }

        return new TdsAuthenticationResult
        {
            IsSspiContinuationNeeded = true,
            SspiToken = sspiToken,
        };
    }

    /// <summary>Creates a failed authentication result.</summary>
    public static TdsAuthenticationResult Fail(string message, uint errorNumber = 18456, byte errorState = 1, byte errorClass = 14)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        return new TdsAuthenticationResult
        {
            IsAuthenticated = false,
            ErrorMessage = message,
            ErrorNumber = errorNumber,
            ErrorState = errorState,
            ErrorClass = errorClass,
        };
    }
}
