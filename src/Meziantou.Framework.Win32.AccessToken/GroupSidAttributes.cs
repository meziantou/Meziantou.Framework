using System;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Win32
{
    [Flags]
    [SuppressMessage("Design", "MA0062:Non-flags enums should not be marked with \"FlagsAttribute\"", Justification = "It is how the enum is defined in Windows")]
    public enum GroupSidAttributes : uint
    {
        SE_GROUP_MANDATORY = 0x00000001,
        SE_GROUP_ENABLED_BY_DEFAULT = 0x00000002,
        SE_GROUP_ENABLED = 0x00000004,
        SE_GROUP_OWNER = 0x00000008,
        SE_GROUP_USE_FOR_DENY_ONLY = 0x00000010,
        SE_GROUP_INTEGRITY = 0x00000020,
        SE_GROUP_INTEGRITY_ENABLED = 0x00000040,

        SE_GROUP_LOGON_ID = 0xC0000000,
        SE_GROUP_RESOURCE = 0x20000000,
        SE_GROUP_VALID_ATTRIBUTES = 0xE000007F,
    }
}
