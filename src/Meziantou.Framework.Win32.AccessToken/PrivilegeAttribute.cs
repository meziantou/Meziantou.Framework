using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

/// <summary>Specifies attributes for a privilege in an access token.</summary>
[Flags]
public enum PrivilegeAttribute : uint
{
    /// <summary>The privilege is disabled.</summary>
    Disabled,

    /// <summary>The privilege is enabled.</summary>
    Enabled = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,

    /// <summary>The privilege is enabled by default.</summary>
    EnabledByDefault = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED_BY_DEFAULT,

    /// <summary>The privilege has been removed from the token.</summary>
    Removed = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_REMOVED,

    /// <summary>The privilege was used to gain access to an object or service.</summary>
    UsedForAccess = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_USED_FOR_ACCESS,
}
