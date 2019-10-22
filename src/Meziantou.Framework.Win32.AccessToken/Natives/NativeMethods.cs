using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Win32.Natives
{
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
        internal static extern bool DuplicateToken(IntPtr tokenHandle, SecurityImpersonationLevel ImpersonationLevel, out IntPtr duplicateTokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, ref uint ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool disableAllPrivileges, IntPtr NewState, uint BufferLength, IntPtr PreviousState, ref uint ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CheckTokenMembership(IntPtr TokenHandle, byte[] SidToCheck, ref bool IsMember);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue([MarshalAs(UnmanagedType.LPTStr)] string? lpSystemName, [MarshalAs(UnmanagedType.LPTStr)] string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal extern static int LookupAccountSid(string? systemName, IntPtr pSid, StringBuilder szName, ref int nameSize, StringBuilder szDomain, ref int domainSize, ref int eUse);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool ConvertSidToStringSid(IntPtr sid, [MarshalAs(UnmanagedType.LPTStr)] out string pStringSid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeName(string? lpSystemName, ref LUID lpLuid, StringBuilder? lpName, ref int cchName);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool ConvertStringSidToSid([In, MarshalAs(UnmanagedType.LPTStr)] string pStringSid, ref IntPtr sid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool CreateWellKnownSid(int sidType, IntPtr domainSid, IntPtr resultSid, ref uint resultSidLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        internal static string LookupPrivilegeName(LUID luid)
        {
            var luidNameLen = 0;
            LookupPrivilegeName(lpSystemName: null, ref luid, lpName: null, ref luidNameLen);
            var sb = new StringBuilder(luidNameLen);
            if (LookupPrivilegeName(lpSystemName: null, ref luid, sb, ref luidNameLen))
                return sb.ToString();

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
}
