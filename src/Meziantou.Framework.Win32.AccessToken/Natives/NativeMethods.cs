using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

internal static class NativeMethods
{
    internal const uint SE_PRIVILEGE_ENABLED_BY_DEFAULT = 1;
    internal const uint SE_PRIVILEGE_ENABLED = 2;
    internal const uint SE_PRIVILEGE_REMOVED = 4;
    internal const uint SE_PRIVILEGE_USED_FOR_ACCESS = 0x80000000;

    internal const int ERROR_INVALID_HANDLE = 0x6;
    internal const int ERROR_BAD_LENGTH = 0x18;
    internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
    internal const int ERROR_NONE_MAPPED = 0x534;

    [DllImport("advapi32.dll", SetLastError = true)]
    internal extern static bool OpenProcessToken(IntPtr processhandle, TokenAccessLevels desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal extern static bool OpenThreadToken(IntPtr threadhandle, TokenAccessLevels desiredAccess, bool openAsSelf, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(IntPtr tokenHandle, TokenInformationClass tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(IntPtr tokenHandle, TokenInformationClass tokenInformationClass, out TokenElevationType tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(IntPtr tokenHandle, TokenInformationClass tokenInformationClass, out TokenType tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool IsTokenRestricted(IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DuplicateToken(IntPtr tokenHandle, SecurityImpersonationLevel impersonationLevel, out IntPtr duplicateTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, ref uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, IntPtr newState, uint bufferLength, IntPtr previousState, ref uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CheckTokenMembership(IntPtr tokenHandle, byte[] sidToCheck, ref bool isMember);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern bool LookupPrivilegeValueW([MarshalAs(UnmanagedType.LPWStr)] string? lpSystemName, [MarshalAs(UnmanagedType.LPWStr)] string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal extern static int LookupAccountSidW(string? systemName, IntPtr pSid, [Out] char[] szName, ref int nameSize, [Out] char[] szDomain, ref int domainSize, ref int eUse);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    internal static extern bool ConvertSidToStringSidW(IntPtr sid, [MarshalAs(UnmanagedType.LPWStr)] out string pStringSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool LookupPrivilegeNameW(string? lpSystemName, ref LUID lpLuid, [Out] char[]? lpName, ref int cchName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool ConvertStringSidToSidW([In, MarshalAs(UnmanagedType.LPWStr)] string pStringSid, ref IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CreateWellKnownSid(int sidType, IntPtr domainSid, IntPtr resultSid, ref uint resultSidLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    internal static string LookupPrivilegeName(LUID luid)
    {
        var luidNameLen = 0;
        LookupPrivilegeNameW(lpSystemName: null, ref luid, lpName: null, ref luidNameLen);

        var name = new char[luidNameLen];
        if (LookupPrivilegeNameW(lpSystemName: null, ref luid, name, ref luidNameLen))
            return new string(name);

        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal enum SID_NAME_USE
    {
        SidTypeUser = 1,
        SidTypeGroup,
        SidTypeDomain,
        SidTypeAlias,
        SidTypeWellKnownGroup,
        SidTypeDeletedAccount,
        SidTypeInvalid,
        SidTypeUnknown,
        SidTypeComputer,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_ELEVATION
    {
        public bool TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LUID
    {
        public uint LowPart;
        public uint HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_LINKED_TOKEN
    {
        public IntPtr LinkedToken;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_OWNER
    {
        public IntPtr Owner;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_GROUPS
    {
        public int GroupCount;

        [MarshalAs(UnmanagedType.ByValArray)]
        public SID_AND_ATTRIBUTES[] Groups;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;

        [MarshalAs(UnmanagedType.ByValArray)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }
}
