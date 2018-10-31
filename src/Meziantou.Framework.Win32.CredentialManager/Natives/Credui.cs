using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Win32.Natives
{
    internal static class Credui
    {
        internal const int CREDUI_MAX_USERNAME_LENGTH = 513;

        [DllImport("credui.dll")]
        internal static extern CredentialUIReturnCodes CredUICmdLinePromptForCredentials(
            string targetName,
            IntPtr reserved1,
            int iError,
            StringBuilder userName,
            int maxUserName,
            StringBuilder password,
            int maxPassword,
            [MarshalAs(UnmanagedType.Bool)] ref bool pfSave,
            CredentialUIFlags flags);

        [DllImport("credui.dll", EntryPoint = "CredUIParseUserNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern CredentialUIReturnCodes CredUIParseUserName(string userName, StringBuilder user, int userMaxChars, StringBuilder domain, int domainMaxChars);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredPackAuthenticationBuffer(int dwFlags, StringBuilder pszUserName, StringBuilder pszPassword, IntPtr pPackedCredentials, ref int pcbPackedCredentials);

        [DllImport("credui.dll", CharSet = CharSet.Auto)]
        internal static extern bool CredUnPackAuthenticationBuffer(int dwFlags,
            IntPtr pAuthBuffer,
            uint cbAuthBuffer,
            StringBuilder pszUserName,
            ref int pcchMaxUserName,
            StringBuilder pszDomainName,
            ref int pcchMaxDomainame,
            StringBuilder pszPassword,
            ref int pcchMaxPassword);

        [DllImport("credui.dll", EntryPoint = "CredUIPromptForWindowsCredentialsW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int CredUIPromptForWindowsCredentials(ref CredentialUIInfo creditUR,
            int authError,
            ref uint authPackage,
            IntPtr inAuthBuffer,
            int inAuthBufferSize,
            out IntPtr refOutAuthBuffer,
            out uint refOutAuthBufferSize,
            ref bool fSave,
            PromptForWindowsCredentialsFlags flags);
    }
}
