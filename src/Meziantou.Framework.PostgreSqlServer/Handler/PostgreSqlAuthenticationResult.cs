namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents the result of an authentication attempt.</summary>
public sealed class PostgreSqlAuthenticationResult
{
    private PostgreSqlAuthenticationResult()
    {
    }

    /// <summary>Gets a value indicating whether authentication succeeded.</summary>
    public bool IsAuthenticated { get; private init; }

    /// <summary>Gets the SQLSTATE error code sent on failure.</summary>
    public string ErrorCode { get; private init; } = "28P01";

    /// <summary>Gets the error message to send to the client on failure.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Creates a successful authentication result.</summary>
    public static PostgreSqlAuthenticationResult Success()
    {
        return new PostgreSqlAuthenticationResult
        {
            IsAuthenticated = true,
        };
    }

    /// <summary>Creates a failed authentication result.</summary>
    public static PostgreSqlAuthenticationResult Fail(string message, string errorCode = "28P01")
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentException.ThrowIfNullOrEmpty(errorCode);

        return new PostgreSqlAuthenticationResult
        {
            IsAuthenticated = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
        };
    }
}
