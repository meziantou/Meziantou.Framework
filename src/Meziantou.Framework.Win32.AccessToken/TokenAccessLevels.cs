namespace Meziantou.Framework.Win32;

/// <summary>Specifies the access rights for an access token.</summary>
/// <remarks>
/// These flags specify the operations that can be performed on an access token.
/// They are used when opening a token with <see cref="AccessToken.OpenCurrentProcessToken"/> or <see cref="AccessToken.OpenProcessToken"/>.
/// </remarks>
[Flags]
[SuppressMessage("Usage", "CA2217:Do not mark enums with FlagsAttribute", Justification = "Values from Windows definition")]
public enum TokenAccessLevels
{
    /// <summary>Required to attach a primary token to a process.</summary>
    AssignPrimary = 0x00000001,

    /// <summary>Required to duplicate an access token.</summary>
    Duplicate = 0x00000002,

    /// <summary>Required to attach an impersonation access token to a process.</summary>
    Impersonate = 0x00000004,

    /// <summary>Required to query an access token.</summary>
    Query = 0x00000008,

    /// <summary>Required to query the source of an access token.</summary>
    QuerySource = 0x00000010,

    /// <summary>Required to enable or disable the privileges in an access token.</summary>
    AdjustPrivileges = 0x00000020,

    /// <summary>Required to adjust the attributes of the groups in an access token.</summary>
    AdjustGroups = 0x00000040,

    /// <summary>Required to change the default owner, primary group, or DACL of an access token.</summary>
    AdjustDefault = 0x00000080,

    /// <summary>Required to adjust the session ID of an access token.</summary>
    AdjustSessionId = 0x00000100,

    /// <summary>Combines STANDARD_RIGHTS_READ and <see cref="Query"/>.</summary>
    Read = 0x00020000 | Query,

    /// <summary>Combines STANDARD_RIGHTS_WRITE with <see cref="AdjustPrivileges"/>, <see cref="AdjustGroups"/>, and <see cref="AdjustDefault"/>.</summary>
    Write = 0x00020000 | AdjustPrivileges | AdjustGroups | AdjustDefault,

    /// <summary>Combines all possible access rights for a token.</summary>
    AllAccess = 0x000F0000 |
        AssignPrimary |
        Duplicate |
        Impersonate |
        Query |
        QuerySource |
        AdjustPrivileges |
        AdjustGroups |
        AdjustDefault |
        AdjustSessionId,

    /// <summary>Requests the maximum access allowed for the caller.</summary>
    MaximumAllowed = 0x02000000,
}
