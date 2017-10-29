using Meziantou.Framework.Win32.Natives;
using System;

namespace Meziantou.Framework.Win32
{
    [Flags]
    public enum PrivilegeAttribute : uint
    {
        Disabled,
        Enabled = NativeMethods.SE_PRIVILEGE_ENABLED,
        EnabledByDefault = NativeMethods.SE_PRIVILEGE_ENABLED_BY_DEFAULT,
        Removed = NativeMethods.SE_PRIVILEGE_REMOVED,
        UsedForAccess = NativeMethods.SE_PRIVILEGE_USED_FOR_ACCESS
    }
}
