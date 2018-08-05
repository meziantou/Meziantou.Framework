using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    public static class CredentialManager
    {
        public static Credential ReadCredential(string applicationName)
        {
            var read = Advapi32.CredRead(applicationName, CredentialType.Generic, 0, out var nCredPtr);
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

            string comment = null;
            if (credential.Comment != IntPtr.Zero)
            {
                comment = Marshal.PtrToStringUni(credential.Comment);
            }

            return new Credential(credential.Type, applicationName, userName, secret, comment);
        }

        public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence)
        {
            WriteCredential(applicationName, userName, secret, null, persistence);
        }

        public static void WriteCredential(string applicationName, string userName, string secret, string comment, CredentialPersistence persistence)
        {
            if (applicationName == null)
                throw new ArgumentNullException(nameof(applicationName));

            if (userName == null)
                throw new ArgumentNullException(nameof(userName));

            if (secret == null)
                throw new ArgumentNullException(nameof(secret));

            // CRED_MAX_CREDENTIAL_BLOB_SIZE 
            // XP and Vista: 512; 
            // 7 and above: 5*512
            var secretLength = secret.Length * UnicodeEncoding.CharSize;
            if (Environment.OSVersion.Version < new Version(6, 1) /* Windows 7 */)
            {
                if (secretLength > 512)
                    throw new ArgumentOutOfRangeException(nameof(secret), "The secret message has exceeded 512 bytes.");
            }
            else
            {
                if (secretLength > 2560)
                    throw new ArgumentOutOfRangeException(nameof(secret), "The secret message has exceeded 2560 bytes.");
            }

            if (comment != null)
            {
                // CRED_MAX_STRING_LENGTH 256
                if (comment.Length > 255)
                    throw new ArgumentOutOfRangeException(nameof(comment), "The comment message has exceeded 256 characters.");
            }

            var commentPtr = IntPtr.Zero;
            var targetNamePtr = IntPtr.Zero;
            var credentialBlobPtr = IntPtr.Zero;
            var userNamePtr = IntPtr.Zero;
            try
            {
                commentPtr = comment != null ? Marshal.StringToCoTaskMemUni(comment) : IntPtr.Zero;
                targetNamePtr = Marshal.StringToCoTaskMemUni(applicationName);
                credentialBlobPtr = Marshal.StringToCoTaskMemUni(secret);
                userNamePtr = Marshal.StringToCoTaskMemUni(userName);

                var credential = new CREDENTIAL
                {
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    Comment = commentPtr,
                    TargetAlias = IntPtr.Zero,
                    Type = CredentialType.Generic,
                    Persist = persistence,
                    CredentialBlobSize = (uint)secretLength,
                    TargetName = targetNamePtr,
                    CredentialBlob = credentialBlobPtr,
                    UserName = userNamePtr
                };

                var written = Advapi32.CredWrite(ref credential, 0);
                var lastError = Marshal.GetLastWin32Error();
                if (!written)
                    throw new Win32Exception(lastError, $"CredWrite failed with the error code {lastError}.");
            }
            finally
            {
                FreeCoTaskMem(commentPtr);
                FreeCoTaskMem(targetNamePtr);
                FreeCoTaskMem(credentialBlobPtr);
                FreeCoTaskMem(userNamePtr);
            }
        }

        public static void DeleteCredential(string applicationName)
        {
            if (applicationName == null)
                throw new ArgumentNullException(nameof(applicationName));

            var success = Advapi32.CredDelete(applicationName, CredentialType.Generic, 0);
            if (!success)
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastError);
            }
        }

        public static IReadOnlyList<Credential> EnumerateCrendentials()
        {
            return EnumerateCrendentials(null);
        }

        public static IReadOnlyList<Credential> EnumerateCrendentials(string filter)
        {
            var result = new List<Credential>();
            var ret = Advapi32.CredEnumerate(filter, 0, out var count, out var pCredentials);
            try
            {
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
            }
            finally
            {
                Advapi32.CredFree(pCredentials);
            }

            return result;
        }

        private static void FreeCoTaskMem(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return;

            Marshal.FreeCoTaskMem(ptr);
        }
    }
}