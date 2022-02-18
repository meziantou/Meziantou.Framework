using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Meziantou.Framework.Win32
{
    [SupportedOSPlatform("windows")]
    public static class LsaPrivateData
    {
        public static void RemoveValue(string key)
        {
            SetValue(key, value: null);
        }

        public static void SetValue(string key, string? value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (key.Length == 0)
                throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

            var objectAttributes = new LSA_OBJECT_ATTRIBUTES();
            var localsystem = new LSA_UNICODE_STRING();
            var secretName = new LSA_UNICODE_STRING(key);

            var lusSecretData = !string.IsNullOrEmpty(value) ? new LSA_UNICODE_STRING(value) : default;

            var lsaPolicyHandle = GetLsaPolicy(ref objectAttributes, ref localsystem);

            var result = LsaStorePrivateData(lsaPolicyHandle, ref secretName, ref lusSecretData);
            ReleaseLsaPolicy(lsaPolicyHandle);

            var winErrorCode = LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception(winErrorCode, "StorePrivateData failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
        }

        public static string? GetValue(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (key.Length == 0)
                throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

            var objectAttributes = new LSA_OBJECT_ATTRIBUTES();
            var localsystem = new LSA_UNICODE_STRING();
            var secretName = new LSA_UNICODE_STRING(key);

            // Get LSA policy
            var lsaPolicyHandle = GetLsaPolicy(ref objectAttributes, ref localsystem);

            var result = LsaRetrievePrivateData(lsaPolicyHandle, ref secretName, out var privateData);
            ReleaseLsaPolicy(lsaPolicyHandle);

            if (result == STATUS_OBJECT_NAME_NOT_FOUND)
                return null;

            var winErrorCode = LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception(winErrorCode, "LsaRetrievePrivateData failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

            if (privateData == IntPtr.Zero)
                return null;

            var lusSecretData = Marshal.PtrToStructure<LSA_UNICODE_STRING>(privateData);
            var value = Marshal.PtrToStringAuto(lusSecretData.Buffer)?[..(lusSecretData.Length / UnicodeEncoding.CharSize)];

            FreeMemory(privateData);

            return value;
        }

        private static IntPtr GetLsaPolicy(ref LSA_OBJECT_ATTRIBUTES objectAttributes, ref LSA_UNICODE_STRING localsystem)
        {
            var ntsResult = LsaOpenPolicy(ref localsystem, ref objectAttributes, (uint)LSA_AccessPolicy.POLICY_GET_PRIVATE_INFORMATION, out var lsaPolicyHandle);
            var winErrorCode = LsaNtStatusToWinError(ntsResult);
            if (winErrorCode != 0)
                throw new Win32Exception(winErrorCode, "LsaOpenPolicy failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));

            return lsaPolicyHandle;
        }

        private static void ReleaseLsaPolicy(IntPtr lsaPolicyHandle)
        {
            var ntsResult = LsaClose(lsaPolicyHandle);
            var winErrorCode = LsaNtStatusToWinError(ntsResult);
            if (winErrorCode != 0)
                throw new Win32Exception(winErrorCode, "LsaClose failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
        }

        private static void FreeMemory(IntPtr buffer)
        {
            var ntsResult = LsaFreeMemory(buffer);
            var winErrorCode = LsaNtStatusToWinError(ntsResult);
            if (winErrorCode != 0)
                throw new Win32Exception(winErrorCode, "LsaFreeMemory failed: " + winErrorCode.ToString(CultureInfo.InvariantCulture));
        }

        private const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_UNICODE_STRING
        {
            public LSA_UNICODE_STRING(string value)
            {
                Buffer = Marshal.StringToHGlobalUni(value);
                Length = (ushort)(value.Length * UnicodeEncoding.CharSize);
                MaximumLength = (ushort)((value.Length + 1) * UnicodeEncoding.CharSize);
            }

            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public LSA_UNICODE_STRING ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        private enum LSA_AccessPolicy : long
        {
            POLICY_VIEW_LOCAL_INFORMATION = 0x00000001L,
            POLICY_VIEW_AUDIT_INFORMATION = 0x00000002L,
            POLICY_GET_PRIVATE_INFORMATION = 0x00000004L,
            POLICY_TRUST_ADMIN = 0x00000008L,
            POLICY_CREATE_ACCOUNT = 0x00000010L,
            POLICY_CREATE_SECRET = 0x00000020L,
            POLICY_CREATE_PRIVILEGE = 0x00000040L,
            POLICY_SET_DEFAULT_QUOTA_LIMITS = 0x00000080L,
            POLICY_SET_AUDIT_REQUIREMENTS = 0x00000100L,
            POLICY_AUDIT_LOG_ADMIN = 0x00000200L,
            POLICY_SERVER_ADMIN = 0x00000400L,
            POLICY_LOOKUP_NAMES = 0x00000800L,
            POLICY_NOTIFICATION = 0x00001000L,
        }

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaRetrievePrivateData(IntPtr policyHandle, ref LSA_UNICODE_STRING keyName, out IntPtr privateData);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaStorePrivateData(IntPtr policyHandle, ref LSA_UNICODE_STRING keyName, ref LSA_UNICODE_STRING privateData);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaOpenPolicy(ref LSA_UNICODE_STRING systemName, ref LSA_OBJECT_ATTRIBUTES objectAttributes, uint desiredAccess, out IntPtr policyHandle);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern int LsaNtStatusToWinError(uint status);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaClose(IntPtr policyHandle);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaFreeMemory(IntPtr buffer);
    }
}
