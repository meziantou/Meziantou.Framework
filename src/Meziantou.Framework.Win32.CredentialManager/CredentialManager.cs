using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security.Credentials;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public static class CredentialManager
{
    public static unsafe Credential? ReadCredential(string applicationName)
    {
        var read = PInvoke.CredRead(applicationName, (uint)CRED_TYPE.CRED_TYPE_GENERIC, 0u, out var handle);
        if (read)
        {
            try
            {
                return ReadCredential(handle);
            }
            finally
            {
                PInvoke.CredFree(handle);
            }
        }

        return null;
    }

    private static unsafe Credential ReadCredential(CREDENTIALW* credential)
    {
        var applicationName = credential->TargetName.ToString();
        Debug.Assert(applicationName != null);

        var userName = credential->UserName.ToString();
        string? secret = null;
        if (credential->CredentialBlob != null)
        {
            secret = Marshal.PtrToStringUni((nint)credential->CredentialBlob, (int)(credential->CredentialBlobSize / UnicodeEncoding.CharSize));
        }

        var comment = credential->Comment.ToString();
        return new Credential((CredentialType)credential->Type, applicationName, userName, secret, comment);
    }

    public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence)
    {
        WriteCredential(applicationName, userName, secret, comment: null, persistence);
    }

    public static unsafe void WriteCredential(string applicationName, string userName, string secret, string? comment, CredentialPersistence persistence)
    {
        if (applicationName is null)
            throw new ArgumentNullException(nameof(applicationName));

        if (userName is null)
            throw new ArgumentNullException(nameof(userName));

        if (secret is null)
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

        fixed (char* applicationNamePtr = applicationName)
        fixed (char* userNamePtr = userName)
        fixed (char* commentPtr = comment)
        fixed (char* secretPtr = secret)
        {
            var credential = new CREDENTIALW
            {
                AttributeCount = 0u,
                Attributes = null,
                Comment = commentPtr,
                TargetAlias = default,
                Type = CRED_TYPE.CRED_TYPE_GENERIC,
                Persist = (CRED_PERSIST)persistence,
                CredentialBlobSize = (uint)secretLength,
                TargetName = applicationNamePtr,
                CredentialBlob = (byte*)secretPtr,
                UserName = userNamePtr,
            };

            var written = PInvoke.CredWrite(in credential, Flags: 0);
            if (!written)
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastError, $"CredWrite failed with the error code {lastError.ToString(CultureInfo.InvariantCulture)}.");
            }
        }
    }

    public static void DeleteCredential(string applicationName)
    {
        if (applicationName is null)
            throw new ArgumentNullException(nameof(applicationName));

        var success = PInvoke.CredDelete(applicationName, (uint)CRED_TYPE.CRED_TYPE_GENERIC, Flags: 0);
        if (!success)
        {
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError);
        }
    }

    [Obsolete("Use EnumerateCredentials")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<Credential> EnumerateCrendentials() => EnumerateCredentials();

    public static IReadOnlyList<Credential> EnumerateCredentials()
    {
        return EnumerateCredentials(filter: null);
    }

    [Obsolete("Use EnumerateCredentials")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<Credential> EnumerateCrendentials(string? filter) => EnumerateCredentials(filter);

    public static unsafe IReadOnlyList<Credential> EnumerateCredentials(string? filter)
    {
        var count = 0u;
        CREDENTIALW** credentials = default;
        fixed (char* filterLocal = filter)
        {
            var ret = PInvoke.CredEnumerate(filterLocal, Flags: default, &count, &credentials);
            try
            {
                if (!ret)
                {
                    var lastError = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastError);
                }

                var result = new Credential[count];
                for (uint i = 0; i < count; i++)
                {
                    result[i] = ReadCredential(credentials[i]);

                }

                return result;
            }
            finally
            {
                if (credentials != null)
                {
                    PInvoke.CredFree(credentials);
                }
            }
        }
    }

    public static unsafe CredentialResult PromptForCredentialsConsole(string target, string? userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
    {
        var userId = new char[(int)PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1]; // Include the null-terminating character
        userId[0] = '\0';
        var userPassword = new char[(int)PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1];
        userPassword[0] = '\0';

        if (!string.IsNullOrEmpty(userName))
        {
            if (userName.Length >= PInvoke.CREDUI_MAX_USERNAME_LENGTH)
                throw new ArgumentException("userName must be less than " + PInvoke.CREDUI_MAX_USERNAME_LENGTH + " characters", nameof(userName));

            userName.AsSpan().CopyTo(userId);
            userId[userName.Length] = '\0';
        }

        BOOL save = saveCredential == CredentialSaveOption.Selected;
        var flags = CREDUI_FLAGS.CREDUI_FLAGS_COMPLETE_USERNAME | CREDUI_FLAGS.CREDUI_FLAGS_EXCLUDE_CERTIFICATES | CREDUI_FLAGS.CREDUI_FLAGS_GENERIC_CREDENTIALS;
        if (saveCredential == CredentialSaveOption.Unselected)
        {
            flags |= CREDUI_FLAGS.CREDUI_FLAGS_SHOW_SAVE_CHECK_BOX | CREDUI_FLAGS.CREDUI_FLAGS_DO_NOT_PERSIST;
        }
        else if (saveCredential == CredentialSaveOption.Selected)
        {
            flags |= CREDUI_FLAGS.CREDUI_FLAGS_SHOW_SAVE_CHECK_BOX | CREDUI_FLAGS.CREDUI_FLAGS_PERSIST;
        }
        else
        {
            flags |= CREDUI_FLAGS.CREDUI_FLAGS_DO_NOT_PERSIST;
        }

        fixed (char* targetPtr = target)
        fixed (char* userIdPtr = userId)
        fixed (char* passwordPtr = userPassword)
        {
            var error = (WIN32_ERROR)PInvoke.CredUICmdLinePromptForCredentials(targetPtr, (SecHandle*)null, 0u, userIdPtr, (uint)userId.Length, passwordPtr, (uint)userPassword.Length, &save, flags);
            if (error is WIN32_ERROR.ERROR_INVALID_FLAGS or WIN32_ERROR.ERROR_INVALID_PARAMETER or WIN32_ERROR.ERROR_NO_SUCH_LOGON_SESSION)
                throw new Win32Exception((int)error);

            fixed (char* userBuilder = new char[(int)PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1])
            fixed (char* domainBuilder = new char[(int)PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1])
            {
                var credentialSaved = saveCredential == CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);

                error = (WIN32_ERROR)PInvoke.CredUIParseUserName(new PWSTR(userIdPtr), userBuilder, PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1, domainBuilder, PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1);
                return error switch
                {
                    WIN32_ERROR.NO_ERROR or WIN32_ERROR.DNS_ERROR_RCODE_NO_ERROR => new CredentialResult(new PWSTR(userBuilder).ToString(), new PWSTR(passwordPtr).ToString(), new PWSTR(domainBuilder).ToString(), credentialSaved),
                    WIN32_ERROR.ERROR_INVALID_ACCOUNT_NAME => new CredentialResult(new PWSTR(userIdPtr).ToString(), new PWSTR(passwordPtr).ToString(), domain: null, credentialSaved),
                    WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER => throw new Win32Exception((int)error, "Insufficient buffer"),
                    WIN32_ERROR.ERROR_INVALID_PARAMETER => throw new Win32Exception((int)error, "Invalid parameter"),
                    _ => throw new Win32Exception((int)error),
                };
            }
        }
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public static unsafe CredentialResult? PromptForCredentials(IntPtr owner = default, string? messageText = null, string? captionText = null, string? userName = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected)
    {
        fixed (char* messageTextPtr = messageText)
        fixed (char* captionTextPtr = captionText)
        {
            var credUI = new CREDUI_INFOW
            {
                cbSize = (uint)Marshal.SizeOf<CREDUI_INFOW>(),
                hwndParent = (HWND)owner,
                pszMessageText = messageTextPtr,
                pszCaptionText = captionTextPtr,
                hbmBanner = HBITMAP.Null,
            };

            BOOL save = saveCredential == CredentialSaveOption.Selected;

            // Setup the flags and variables
            var errorcode = 0u;

            var flags = CREDUIWIN_FLAGS.CREDUIWIN_GENERIC | CREDUIWIN_FLAGS.CREDUIWIN_ENUMERATE_CURRENT_USER;
            if (saveCredential != CredentialSaveOption.Hidden)
            {
                flags |= CREDUIWIN_FLAGS.CREDUIWIN_CHECKBOX;
            }

            // Prefill username
            GetInputBuffer(userName, out var inCredBuffer, out var inCredSize);

            // Setup the flags and variables
            uint authPackage = 0;
            void* outCredBuffer = default;
            uint outCredBufferSize = default;
            var result = PInvoke.CredUIPromptForWindowsCredentials(
                &credUI,
                errorcode,
                &authPackage,
                inCredBuffer,
                inCredSize,
                &outCredBuffer,
                &outCredBufferSize,
                &save,
                flags);

            FreeCoTaskMem((nint)inCredBuffer);

            if (result == 0 && GetCredentialsFromOutputBuffer(outCredBuffer, outCredBufferSize, out userName, out var password, out var domain))
            {
                var credentialSaved = saveCredential == CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);
                return new CredentialResult(userName, password, domain, credentialSaved);
            }

            return null;
        }
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private static unsafe void GetInputBuffer(string? user, out byte* inCredBuffer, out uint inCredSize)
    {
        if (!string.IsNullOrEmpty(user))
        {
            fixed (char* userPtr = user)
            {
                inCredSize = 1024;
                inCredBuffer = (byte*)Marshal.AllocCoTaskMem((int)inCredSize);
                if (PInvoke.CredPackAuthenticationBuffer(default, userPtr, pszPassword: null, inCredBuffer, ref inCredSize))
                    return;
            }
        }

        inCredBuffer = null;
        inCredSize = 0;
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private static unsafe bool GetCredentialsFromOutputBuffer(void* outCredBuffer, uint outCredSize, [NotNullWhen(returnValue: true)] out string? userName, [NotNullWhen(returnValue: true)] out string? password, [NotNullWhen(returnValue: true)] out string? domain)
    {
        var maxUserName = PInvoke.CREDUI_MAX_USERNAME_LENGTH;
        var maxDomain = PInvoke.CREDUI_MAX_USERNAME_LENGTH;
        var maxPassword = PInvoke.CREDUI_MAX_USERNAME_LENGTH;
        fixed (char* usernameBuf = new char[maxUserName])
        fixed (char* passwordBuf = new char[maxDomain])
        fixed (char* domainBuf = new char[maxPassword])
        {
            try
            {
                if (PInvoke.CredUnPackAuthenticationBuffer(default, outCredBuffer, outCredSize, usernameBuf, ref maxUserName, domainBuf, &maxDomain, passwordBuf, ref maxPassword))
                {
                    userName = new PWSTR(usernameBuf).ToString();
                    password = new PWSTR(passwordBuf).ToString();
                    domain = new PWSTR(domainBuf).ToString();

                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        usernameBuf[0] = '\0';
                        domainBuf[0] = '\0';

                        var returnCode = (WIN32_ERROR)PInvoke.CredUIParseUserName(userName, usernameBuf, PInvoke.CREDUI_MAX_USERNAME_LENGTH, domainBuf, PInvoke.CREDUI_MAX_USERNAME_LENGTH);
                        switch (returnCode)
                        {
                            case WIN32_ERROR.NO_ERROR:
                                userName = new PWSTR(usernameBuf).ToString();
                                domain = new PWSTR(domainBuf).ToString();
                                break;

                            case WIN32_ERROR.ERROR_INVALID_ACCOUNT_NAME:
                                break;

                            case WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER:
                                throw new Win32Exception((int)returnCode, "Insufficient buffer");

                            case WIN32_ERROR.ERROR_INVALID_PARAMETER:
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
                Marshal.Copy(zeroBytes, 0, (nint)outCredBuffer, (int)outCredSize);
                FreeCoTaskMem((nint)outCredBuffer);
            }
        }
    }

    private static void FreeCoTaskMem(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return;

        Marshal.FreeCoTaskMem(ptr);
    }
}
