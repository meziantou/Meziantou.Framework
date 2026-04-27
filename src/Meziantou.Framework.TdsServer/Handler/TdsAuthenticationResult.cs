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

    /// <summary>Creates a successful authentication result.</summary>
    public static TdsAuthenticationResult Success(string? database = null)
    {
        return new TdsAuthenticationResult
        {
            IsAuthenticated = true,
            Database = database,
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
