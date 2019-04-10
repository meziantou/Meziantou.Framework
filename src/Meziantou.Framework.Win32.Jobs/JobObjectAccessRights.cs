using System;

namespace Meziantou.Framework.Win32
{
    [Flags]
    public enum JobObjectAccessRights
    {
        AllAccess = 0x1f001f,
        AssignProcess = 0x0001,
        Query = 0x0004,
        SetAttributes = 0x0002,
        SetSecurityAttributes = 0x0010,
        Terminate = 0x0008,
    }
}
