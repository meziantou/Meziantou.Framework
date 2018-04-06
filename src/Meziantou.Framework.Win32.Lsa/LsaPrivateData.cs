using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Win32.Lsa
{
    public class LsaPrivateData
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_UNICODE_STRING
        {
            public UInt16 Length;
            public UInt16 MaximumLength;
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
            POLICY_NOTIFICATION = 0x00001000L
        }

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaRetrievePrivateData(IntPtr PolicyHandle, ref LSA_UNICODE_STRING KeyName, out IntPtr PrivateData);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaStorePrivateData(IntPtr policyHandle, ref LSA_UNICODE_STRING KeyName, ref LSA_UNICODE_STRING PrivateData);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaOpenPolicy(ref LSA_UNICODE_STRING SystemName, ref LSA_OBJECT_ATTRIBUTES ObjectAttributes, uint DesiredAccess, out IntPtr PolicyHandle);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaNtStatusToWinError(uint status);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaClose(IntPtr policyHandle);

        [DllImport("advapi32.dll", SetLastError = true, PreserveSig = true)]
        private static extern uint LsaFreeMemory(IntPtr buffer);

        private static void ReleaseLsaPolicy(IntPtr LsaPolicyHandle)
        {
            uint ntsResult = LsaClose(LsaPolicyHandle);
            uint winErrorCode = LsaNtStatusToWinError(ntsResult);
            if (winErrorCode != 0)
            {
                throw new Exception("LsaClose failed: " + winErrorCode);
            }
        }

        public static void SetValue(string key, string value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (key.Length == 0)
                throw new ArgumentException($"{nameof(key)} must not be empty", nameof(key));

            var objectAttributes = new LSA_OBJECT_ATTRIBUTES
            {
                Length = 0,
                RootDirectory = IntPtr.Zero,
                Attributes = 0,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero
            };

            var localsystem = new LSA_UNICODE_STRING
            {
                Buffer = IntPtr.Zero,
                Length = 0,
                MaximumLength = 0
            };

            var secretName = new LSA_UNICODE_STRING
            {
                Buffer = Marshal.StringToHGlobalUni(key),
                Length = (ushort)(key.Length * UnicodeEncoding.CharSize),
                MaximumLength = (ushort)((key.Length + 1) * UnicodeEncoding.CharSize)
            };

            var lusSecretData = new LSA_UNICODE_STRING();
            if (value.Length > 0)
            {
                // Create data and key
                lusSecretData.Buffer = Marshal.StringToHGlobalUni(value);
                lusSecretData.Length = (UInt16)(value.Length * UnicodeEncoding.CharSize);
                lusSecretData.MaximumLength = (UInt16)((value.Length + 1) * UnicodeEncoding.CharSize);
            }
            else
            {
                // Delete data and key
                lusSecretData.Buffer = IntPtr.Zero;
                lusSecretData.Length = 0;
                lusSecretData.MaximumLength = 0;
            }

            // Get LSA policy
            uint ntsResult = LsaOpenPolicy(ref localsystem, ref objectAttributes, (uint)LSA_AccessPolicy.POLICY_CREATE_SECRET, out var lsaPolicyHandle);
            uint winErrorCode = LsaNtStatusToWinError(ntsResult);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode, "LsaOpenPolicy failed: " + winErrorCode);

            uint result = LsaStorePrivateData(lsaPolicyHandle, ref secretName, ref lusSecretData);
            ReleaseLsaPolicy(lsaPolicyHandle);

            winErrorCode = LsaNtStatusToWinError(result);
            if (winErrorCode != 0)
                throw new Win32Exception((int)winErrorCode, "StorePrivateData failed: " + winErrorCode);
        }
    }
}
