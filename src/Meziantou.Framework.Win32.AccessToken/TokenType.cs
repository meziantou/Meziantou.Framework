namespace Meziantou.Framework.Win32;

/// <summary>Specifies the type of access token.</summary>
public enum TokenType
{
    /// <summary>A primary token is associated with a process and represents the security context of the process.</summary>
    TokenPrimary = 1,

    /// <summary>An impersonation token is associated with a thread and represents a client's security context.</summary>
    TokenImpersonation,
}
