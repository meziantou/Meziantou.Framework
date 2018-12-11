using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    internal static class Advapi32
    {
        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, CredentialType type, int reservedFlag, out CredentialSafeHandle credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, CredentialType type, int reservedFlag);

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredEnumerate(string filter, int flag, out int count, out CredentialSafeHandle pCredentials);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        public static extern bool CredFree([In] IntPtr cred);
    }
}
