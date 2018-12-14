using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    public class CredentialResult
    {
        public CredentialResult(string userName, string password, string domain, CredentialSaveOption credentialSaved)
        {
            UserName = userName;
            Password = password;
            Domain = domain;
            CredentialSaved = credentialSaved;
        }

        public string UserName { get; }
        public string Password { get; }
        public string Domain { get; }
        public CredentialSaveOption CredentialSaved { get; }
    }

    public static class CredentialManager
    {
        public static Credential ReadCredential(string applicationName)
        {
            var read = Advapi32.CredRead(applicationName, CredentialType.Generic, 0, out var handle);
            using (handle)
            {
                if (read)
                {
                    var cred = handle.GetCredential();
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
            using (pCredentials)
            {
                if (ret && !pCredentials.IsInvalid)
                {
                    for (var n = 0; n < count; n++)
                    {
                        var credential = Marshal.ReadIntPtr(pCredentials.DangerousGetHandle(), n * Marshal.SizeOf<IntPtr>());
                        result.Add(ReadCredential(Marshal.PtrToStructure<CREDENTIAL>(credential)));
                    }
                }
                else
                {
                    var lastError = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastError);
                }
            }

            return result;
        }

        public static CredentialResult PromptForCredentialsConsole(string target, string userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
        {
            var userId = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            var userPassword = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            if (!string.IsNullOrEmpty(userName))
            {
                userId.Append(userName);
            }

            var save = saveCredential == CredentialSaveOption.Selected ? true : false;
            var flags = CredentialUIFlags.CompleteUsername | CredentialUIFlags.ExcludeCertificates | CredentialUIFlags.GenericCredentials;
            if (saveCredential == CredentialSaveOption.Unselected)
            {
                flags |= CredentialUIFlags.ShowSaveCheckBox | CredentialUIFlags.DoNotPersist;
            }
            else if (saveCredential == CredentialSaveOption.Selected)
            {
                flags |= CredentialUIFlags.ShowSaveCheckBox | CredentialUIFlags.Persist;
            }
            else
            {
                flags |= CredentialUIFlags.DoNotPersist;
            }

            var returnCode = Credui.CredUICmdLinePromptForCredentials(target, IntPtr.Zero, 0, userId, userId.Capacity, userPassword, userPassword.Capacity, ref save, flags);

            var userBuilder = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            var domainBuilder = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);

            var credentialSaved = saveCredential == CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);

            returnCode = Credui.CredUIParseUserName(userId.ToString(), userBuilder, userBuilder.Capacity, domainBuilder, domainBuilder.Capacity);
            switch (returnCode)
            {
                case CredentialUIReturnCodes.Success:
                    return new CredentialResult(
                        userBuilder.ToString(),
                        userPassword.ToString(),
                        domainBuilder.ToString(),
                        credentialSaved);

                case CredentialUIReturnCodes.InvalidAccountName:
                    return new CredentialResult(
                        userId.ToString(),
                        userPassword.ToString(),
                        null,
                        credentialSaved);

                case CredentialUIReturnCodes.InsufficientBuffer:
                    throw new OutOfMemoryException();

                case CredentialUIReturnCodes.InvalidParameter:
                    throw new ArgumentException();

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static CredentialResult PromptForCredentials(IntPtr owner = default, string messageText = null, string captionText = null, string userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
        {
            var credUI = new CredentialUIInfo
            {
                hwndParent = owner,
                pszMessageText = messageText,
                pszCaptionText = captionText,
                hbmBanner = IntPtr.Zero,
            };

            var save = saveCredential == CredentialSaveOption.Selected ? true : false;

            // Setup the flags and variables
            credUI.cbSize = Marshal.SizeOf(credUI);
            var errorcode = 0;
            uint authPackage = 0;

            var outCredBuffer = IntPtr.Zero;
            var flags = PromptForWindowsCredentialsFlags.GenericCredentials | PromptForWindowsCredentialsFlags.EnumerateCurrentUser;
            if (saveCredential != CredentialSaveOption.Hidden)
            {
                flags |= PromptForWindowsCredentialsFlags.ShowCheckbox;
            }

            // Prefill username
            GetInputBuffer(userName, out var inCredBuffer, out var inCredSize);

            // Setup the flags and variables
            var result = Credui.CredUIPromptForWindowsCredentials(ref credUI,
                errorcode,
                ref authPackage,
                inCredBuffer,
                inCredSize,
                out outCredBuffer,
                out var outCredSize,
                ref save,
                flags);

            FreeCoTaskMem(inCredBuffer);

            if (result == 0 && GetCredentialsFromOutputBuffer(outCredBuffer, outCredSize, out userName, out var password, out var domain))
            {
                var credentialSaved = saveCredential == CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);
                return new CredentialResult(userName, password, domain, credentialSaved);
            }

            return null;
        }

        private static void GetInputBuffer(string user, out IntPtr inCredBuffer, out int inCredSize)
        {
            if (!string.IsNullOrEmpty(user))
            {
                var usernameBuf = new StringBuilder(user);
                var passwordBuf = new StringBuilder();

                inCredSize = 1024;
                inCredBuffer = Marshal.AllocCoTaskMem(inCredSize);
                if (Credui.CredPackAuthenticationBuffer(0, usernameBuf, passwordBuf, inCredBuffer, ref inCredSize))
                    return;
            }

            inCredBuffer = IntPtr.Zero;
            inCredSize = 0;
        }

        private static bool GetCredentialsFromOutputBuffer(IntPtr outCredBuffer, uint outCredSize, out string userName, out string password, out string domain)
        {
            var maxUserName = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var maxDomain = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var maxPassword = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var usernameBuf = new StringBuilder(maxUserName);
            var passwordBuf = new StringBuilder(maxDomain);
            var domainBuf = new StringBuilder(maxPassword);
            try
            {
                if (Credui.CredUnPackAuthenticationBuffer(0, outCredBuffer, outCredSize, usernameBuf, ref maxUserName, domainBuf, ref maxDomain, passwordBuf, ref maxPassword))
                {
                    userName = usernameBuf.ToString();
                    password = passwordBuf.ToString();
                    domain = domainBuf.ToString();
                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        usernameBuf.Clear();
                        domainBuf.Clear();

                        var returnCode = Credui.CredUIParseUserName(userName, usernameBuf, usernameBuf.Capacity, domainBuf, domainBuf.Capacity);
                        switch (returnCode)
                        {
                            case CredentialUIReturnCodes.Success:
                                userName = usernameBuf.ToString();
                                domain = domainBuf.ToString();
                                break;

                            case CredentialUIReturnCodes.InvalidAccountName:
                                return true;

                            case CredentialUIReturnCodes.InsufficientBuffer:
                                throw new OutOfMemoryException();

                            case CredentialUIReturnCodes.InvalidParameter:
                                throw new ArgumentException();

                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }

                userName = null;
                password = null;
                domain = null;
                return false;
            }
            finally
            {
                //mimic SecureZeroMem function to make sure buffer is zeroed out. SecureZeroMem is not an exported function, neither is RtlSecureZeroMemory
                var zeroBytes = new byte[outCredSize];
                Marshal.Copy(zeroBytes, 0, outCredBuffer, (int)outCredSize);
                FreeCoTaskMem(outCredBuffer);
            }
        }

        private static void FreeCoTaskMem(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return;

            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
