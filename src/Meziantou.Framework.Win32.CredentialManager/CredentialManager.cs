using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Security.Native;

namespace Meziantou.Framework.Security
{
    public static class CredentialManager
    {
        public static Credential ReadCredential(string applicationName)
        {
            IntPtr nCredPtr;
            var read = Advapi32.CredRead(applicationName, CredentialType.Generic, 0, out nCredPtr);
            if (read)
            {
                using (var critCred = new CriticalCredentialHandle(nCredPtr))
                {
                    var cred = critCred.GetCredential();
                    return ReadCredential(cred);
                }
            }

            return null;
        }

        private static Credential ReadCredential(CREDENTIAL credential)
        {
            var applicationName = Marshal.PtrToStringUni(credential.TargetName);
            var userName = Marshal.PtrToStringUni(credential.UserName);
            string secret = null;
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                secret = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }

            return new Credential(credential.Type, applicationName, userName, secret);
        }

        public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence)
        {
            //byte[] byteArray = Encoding.Unicode.GetBytes(secret);
            //if (byteArray.Length > 512)
            //    throw new ArgumentOutOfRangeException("secret", "The secret message has exceeded 512 bytes.");

            var credential = new CREDENTIAL();
            credential.AttributeCount = 0;
            credential.Attributes = IntPtr.Zero;
            credential.Comment = IntPtr.Zero;
            credential.TargetAlias = IntPtr.Zero;
            credential.Type = CredentialType.Generic;
            credential.Persist = persistence;
            credential.CredentialBlobSize = (uint)Encoding.Unicode.GetBytes(secret).Length;
            credential.TargetName = Marshal.StringToCoTaskMemUni(applicationName);
            credential.CredentialBlob = Marshal.StringToCoTaskMemUni(secret);
            credential.UserName = Marshal.StringToCoTaskMemUni(userName);

            var written = Advapi32.CredWrite(ref credential, 0);
            var lastError = Marshal.GetLastWin32Error();

            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
            Marshal.FreeCoTaskMem(credential.UserName);

            if (!written)
            {
                throw new Exception($"CredWrite failed with the error code {lastError}.");
            }
        }

        public static void DeleteCredential(string applicationName)
        {
            var success = Advapi32.CredDelete(applicationName, CredentialType.Generic, 0);
            if (!success)
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastError);
            }
        }

        public static IReadOnlyList<Credential> EnumerateCrendentials()
        {
            var result = new List<Credential>();

            int count;
            IntPtr pCredentials;
            var ret = Advapi32.CredEnumerate(null, 0, out count, out pCredentials);
            if (ret)
            {
                for (var n = 0; n < count; n++)
                {
                    var credential = Marshal.ReadIntPtr(pCredentials, n * Marshal.SizeOf<IntPtr>());
                    result.Add(ReadCredential(Marshal.PtrToStructure<CREDENTIAL>(credential)));
                }
            }
            else
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastError);
            }

            return result;
        }
    }
}