using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    [SupportedOSPlatform("windows")]
    public static class CredentialManager
    {
        public static Credential? ReadCredential(string applicationName)
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
            Debug.Assert(applicationName != null);

            var userName = Marshal.PtrToStringUni(credential.UserName);
            string? secret = null;
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                secret = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }

            var comment = Marshal.PtrToStringUni(credential.Comment);
            return new Credential(credential.Type, applicationName, userName, secret, comment);
        }

        public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence)
        {
            WriteCredential(applicationName, userName, secret, comment: null, persistence);
        }

        public static void WriteCredential(string applicationName!!, string userName!!, string secret!!, string? comment, CredentialPersistence persistence)
        {

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
                    UserName = userNamePtr,
                };

                var written = Advapi32.CredWrite(ref credential, 0);
                var lastError = Marshal.GetLastWin32Error();
                if (!written)
                    throw new Win32Exception(lastError, $"CredWrite failed with the error code {lastError.ToString(CultureInfo.InvariantCulture)}.");
            }
            finally
            {
                FreeCoTaskMem(commentPtr);
                FreeCoTaskMem(targetNamePtr);
                FreeCoTaskMem(credentialBlobPtr);
                FreeCoTaskMem(userNamePtr);
            }
        }

        public static void DeleteCredential(string applicationName!!)
        {
            var success = Advapi32.CredDelete(applicationName, CredentialType.Generic, 0);
            if (!success)
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastError);
            }
        }

        [Obsolete("Use EnumerateCredentials")]
        public static IReadOnlyList<Credential> EnumerateCrendentials() => EnumerateCredentials();

        public static IReadOnlyList<Credential> EnumerateCredentials()
        {
            return EnumerateCredentials(filter: null);
        }

        [Obsolete("Use EnumerateCredentials")]
        public static IReadOnlyList<Credential> EnumerateCrendentials(string? filter) => EnumerateCredentials(filter);

        public static IReadOnlyList<Credential> EnumerateCredentials(string? filter)
        {
            var result = new List<Credential>();
            var ret = Advapi32.CredEnumerate(filter, 0, out var count, out var pCredentials);
            using (pCredentials)
            {
                if (ret && !pCredentials.IsInvalid)
                {
                    for (var n = 0; n < count; n++)
                    {
                        var credentialPtr = Marshal.ReadIntPtr(pCredentials.DangerousGetHandle(), n * Marshal.SizeOf<IntPtr>());
                        var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                        result.Add(ReadCredential(credential));
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

        public static CredentialResult PromptForCredentialsConsole(string target, string? userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
        {
            var userId = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            var userPassword = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            if (!string.IsNullOrEmpty(userName))
            {
                userId.Append(userName);
            }

            var save = saveCredential == CredentialSaveOption.Selected;
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

            _ = Credui.CredUICmdLinePromptForCredentialsW(target, IntPtr.Zero, 0, userId, userId.Capacity, userPassword, userPassword.Capacity, ref save, flags);

            var userBuilder = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);
            var domainBuilder = new StringBuilder(Credui.CREDUI_MAX_USERNAME_LENGTH);

            var credentialSaved = saveCredential == CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);

            var returnCode = Credui.CredUIParseUserName(userId.ToString(), userBuilder, userBuilder.Capacity, domainBuilder, domainBuilder.Capacity);
            return returnCode switch
            {
                CredentialUIReturnCodes.Success => new CredentialResult(userBuilder.ToString(), userPassword.ToString(), domainBuilder.ToString(), credentialSaved),
                CredentialUIReturnCodes.InvalidAccountName => new CredentialResult(userId.ToString(), userPassword.ToString(), domain: null, credentialSaved),
                CredentialUIReturnCodes.InsufficientBuffer => throw new Win32Exception((int)returnCode, "Insufficient buffer"),
                CredentialUIReturnCodes.InvalidParameter => throw new Win32Exception((int)returnCode, "Invalid parameter"),
                _ => throw new Win32Exception((int)returnCode),
            };
        }

        public static CredentialResult? PromptForCredentials(IntPtr owner = default, string? messageText = null, string? captionText = null, string? userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
        {
            var credUI = new CredentialUIInfo
            {
                HwndParent = owner,
                PszMessageText = messageText,
                PszCaptionText = captionText,
                HbmBanner = IntPtr.Zero,
            };

            var save = saveCredential == CredentialSaveOption.Selected;

            // Setup the flags and variables
            credUI.CbSize = Marshal.SizeOf(credUI);
            var errorcode = 0;
            uint authPackage = 0;

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
                out var outCredBuffer,
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

        private static void GetInputBuffer(string? user, out IntPtr inCredBuffer, out int inCredSize)
        {
            if (!string.IsNullOrEmpty(user))
            {
                inCredSize = 1024;
                inCredBuffer = Marshal.AllocCoTaskMem(inCredSize);
                if (Credui.CredPackAuthenticationBuffer(0, user, pszPassword: "", inCredBuffer, ref inCredSize))
                    return;
            }

            inCredBuffer = IntPtr.Zero;
            inCredSize = 0;
        }

        private static bool GetCredentialsFromOutputBuffer(IntPtr outCredBuffer, uint outCredSize, [NotNullWhen(returnValue: true)] out string? userName, [NotNullWhen(returnValue: true)] out string? password, [NotNullWhen(returnValue: true)] out string? domain)
        {
            var maxUserName = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var maxDomain = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var maxPassword = Credui.CREDUI_MAX_USERNAME_LENGTH;
            var usernameBuf = new StringBuilder(maxUserName);
            var passwordBuf = new StringBuilder(maxDomain);
            var domainBuf = new StringBuilder(maxPassword);
            try
            {
                if (Credui.CredUnPackAuthenticationBufferW(0, outCredBuffer, outCredSize, usernameBuf, ref maxUserName, domainBuf, ref maxDomain, passwordBuf, ref maxPassword))
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
                                break;

                            case CredentialUIReturnCodes.InsufficientBuffer:
                                throw new Win32Exception((int)returnCode, "Insufficient buffer");

                            case CredentialUIReturnCodes.InvalidParameter:
                                throw new Win32Exception((int)returnCode, "Invalid parameter");

                            default:
                                throw new Win32Exception((int)returnCode);
                        }
                    }

                    return true;
                }
                else
                {
                    userName = null;
                    password = null;
                    domain = null;
                    return false;
                }
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
