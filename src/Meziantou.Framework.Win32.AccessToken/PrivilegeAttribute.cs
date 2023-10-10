using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

[Flags]
public enum PrivilegeAttribute : uint
{
    Disabled,
    Enabled = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,
    EnabledByDefault = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED_BY_DEFAULT,
    Removed = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_REMOVED,
    UsedForAccess = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_USED_FOR_ACCESS,
}
