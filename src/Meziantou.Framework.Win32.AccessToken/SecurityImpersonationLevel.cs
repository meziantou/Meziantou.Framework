namespace Meziantou.Framework.Win32;

/// <summary>Specifies security impersonation levels, which determine the degree to which a server process can act on behalf of a client process.</summary>
public enum SecurityImpersonationLevel
{
    /// <summary>The server process cannot obtain identification information about the client and cannot impersonate the client.</summary>
    SecurityAnonymous,

    /// <summary>The server process can obtain identification information about the client but cannot impersonate the client.</summary>
    SecurityIdentification,

    /// <summary>The server process can impersonate the client's security context on the local system.</summary>
    SecurityImpersonation,

    /// <summary>The server process can impersonate the client's security context on remote systems.</summary>
    SecurityDelegation,
}
