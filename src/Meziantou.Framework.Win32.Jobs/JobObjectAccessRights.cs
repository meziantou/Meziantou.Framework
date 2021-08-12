using System;

namespace Meziantou.Framework.Win32;

// https://docs.microsoft.com/en-us/windows/win32/procthread/job-object-security-and-access-rights
[Flags]
public enum JobObjectAccessRights
{
    Delete = 0x00010000,
    ReadControl = 0x00020000,
    WriteDAC = 0x00040000,
    WriteOwner = 0x00080000,
    Synchronize = 0x00100000,

    AssignProcess = 0x00000001,
    SetAttributes = 0x00000002,
    Query = 0x00000004,
    Terminate = 0x00000008,
    SetSecurityAttributes = 0x00000010,
    Impersonate = 0x00000020,

    AllAccess = 0x001f003f,
}
