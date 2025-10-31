using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security.Credentials;

namespace Meziantou.Framework.Win32;

/// <summary>Provides methods to manage credentials in the Windows Credential Manager.</summary>
/// <example>
/// <code>
/// // Save a credential
/// CredentialManager.WriteCredential(
///     applicationName: "MyApp",
///     userName: "user@example.com",
///     secret: "password123",
///     comment: "Application credentials",
///     persistence: CredentialPersistence.LocalMachine);
///
/// // Read a credential
/// var credential = CredentialManager.ReadCredential("MyApp");
/// Console.WriteLine(credential?.UserName);
///
/// // Delete a credential
/// CredentialManager.DeleteCredential("MyApp");
/// </code>
/// </example>
[SupportedOSPlatform("windows5.1.2600")]
public static class CredentialManager
{
    /// <summary>Reads a credential from the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <returns>The credential if found; otherwise, <c>null</c>.</returns>
    public static Credential? ReadCredential(string applicationName)
    {
        return ReadCredential(applicationName, CredentialType.Generic);
    }

    /// <summary>Reads a credential from the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="type">The type of the credential.</param>
    /// <returns>The credential if found; otherwise, <c>null</c>.</returns>
    public static unsafe Credential? ReadCredential(string applicationName, CredentialType type)
    {
        var read = PInvoke.CredRead(applicationName, (CRED_TYPE)type, out var handle);
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
        Debug.Assert(applicationName is not null);

        var userName = credential->UserName.ToString();
        string? secret = null;
        if (credential->CredentialBlob is not null)
        {
            secret = Marshal.PtrToStringUni((nint)credential->CredentialBlob, (int)(credential->CredentialBlobSize / UnicodeEncoding.CharSize));
        }

        var comment = credential->Comment.ToString();
        return new Credential((CredentialType)credential->Type, applicationName, userName, secret, comment);
    }

    /// <summary>Writes a credential to the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="secret">The password or secret.</param>
    /// <param name="persistence">The persistence option for the credential.</param>
    public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence)
    {
        WriteCredential(applicationName, userName, secret, comment: null, persistence, CredentialType.Generic);
    }

    /// <summary>Writes a credential to the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="secret">The password or secret.</param>
    /// <param name="persistence">The persistence option for the credential.</param>
    /// <param name="type">The type of the credential.</param>
    public static void WriteCredential(string applicationName, string userName, string secret, CredentialPersistence persistence, CredentialType type)
    {
        WriteCredential(applicationName, userName, secret, comment: null, persistence, type);
    }

    /// <summary>Writes a credential to the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="secret">The password or secret.</param>
    /// <param name="comment">An optional comment describing the credential.</param>
    /// <param name="persistence">The persistence option for the credential.</param>
    public static void WriteCredential(string applicationName, string userName, string secret, string? comment, CredentialPersistence persistence)
    {
        WriteCredential(applicationName, userName, secret, comment, persistence, CredentialType.Generic);
    }

    /// <summary>Writes a credential to the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="secret">The password or secret.</param>
    /// <param name="comment">An optional comment describing the credential.</param>
    /// <param name="persistence">The persistence option for the credential.</param>
    /// <param name="type">The type of the credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="applicationName"/>, <paramref name="userName"/>, or <paramref name="secret"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="secret"/> exceeds the maximum size or <paramref name="comment"/> exceeds 255 characters.</exception>
    public static unsafe void WriteCredential(string applicationName, string userName, string secret, string? comment, CredentialPersistence persistence, CredentialType type)
    {
        ArgumentNullException.ThrowIfNull(applicationName);

        ArgumentNullException.ThrowIfNull(userName);

        ArgumentNullException.ThrowIfNull(secret);

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

        if (comment is not null)
        {
            // CRED_MAX_STRING_LENGTH 256
            if (comment.Length > 255)
                throw new ArgumentOutOfRangeException(nameof(comment), "The comment message has exceeded 256 characters.");
        }

        if (type is not (CredentialType.Generic or CredentialType.DomainPassword))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Only CredentialType.Generic and CredentialType.DomainPassword is supported");
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
                Type = (CRED_TYPE)type,
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

    /// <summary>Deletes a credential from the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    public static void DeleteCredential(string applicationName)
    {
        DeleteCredential(applicationName, CredentialType.Generic);
    }

    /// <summary>Deletes a credential from the Windows Credential Manager.</summary>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="type">The type of the credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="applicationName"/> is <c>null</c>.</exception>
    public static void DeleteCredential(string applicationName, CredentialType type)
    {
        ArgumentNullException.ThrowIfNull(applicationName);

        var success = PInvoke.CredDelete(applicationName, (CRED_TYPE)type);
        if (!success)
        {
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError);
        }
    }

    [Obsolete("Use EnumerateCredentials")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<Credential> EnumerateCrendentials() => EnumerateCredentials();

    /// <summary>Enumerates all credentials from the Windows Credential Manager.</summary>
    /// <returns>A read-only list of credentials.</returns>
    public static IReadOnlyList<Credential> EnumerateCredentials()
    {
        return EnumerateCredentials(filter: null);
    }

    [Obsolete("Use EnumerateCredentials")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<Credential> EnumerateCrendentials(string? filter) => EnumerateCredentials(filter);

    /// <summary>Enumerates credentials from the Windows Credential Manager that match the specified filter.</summary>
    /// <param name="filter">A filter string that supports wildcards. Pass <c>null</c> or "*" to enumerate all credentials.</param>
    /// <returns>A read-only list of credentials matching the filter.</returns>
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
                if (credentials is not null)
                {
                    PInvoke.CredFree(credentials);
                }
            }
        }
    }

    /// <summary>Prompts the user for credentials using a console-based interface.</summary>
    /// <param name="target">The name of the target for the credentials.</param>
    /// <param name="userName">An optional default username.</param>
    /// <param name="saveCredential">Specifies whether the save checkbox should be displayed and its default state.</param>
    /// <returns>A <see cref="CredentialResult"/> containing the entered credentials.</returns>
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

                error = PInvoke.CredUIParseUserName(new PWSTR(userIdPtr), userBuilder, PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1, domainBuilder, PInvoke.CREDUI_MAX_USERNAME_LENGTH + 1);
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

    /// <summary>Prompts the user for credentials using a Windows dialog.</summary>
    /// <param name="owner">The handle of the parent window.</param>
    /// <param name="messageText">The message text to display in the dialog.</param>
    /// <param name="captionText">The caption text to display in the dialog.</param>
    /// <param name="userName">An optional default username.</param>
    /// <param name="saveCredential">Specifies whether the save checkbox should be displayed and its default state.</param>
    /// <returns>A <see cref="CredentialResult"/> if the user entered credentials; otherwise, <c>null</c>.</returns>
    [SupportedOSPlatform("windows6.0.6000")]
    public static unsafe CredentialResult? PromptForCredentials(IntPtr owner, string? messageText, string? captionText, string? userName, CredentialSaveOption saveCredential)
    {
        return PromptForCredentials(owner, messageText, captionText, userName, password: null, saveCredential, CredentialErrorCode.None);
    }

    /// <summary>Prompts the user for credentials using a Windows dialog.</summary>
    /// <param name="owner">The handle of the parent window.</param>
    /// <param name="messageText">The message text to display in the dialog.</param>
    /// <param name="captionText">The caption text to display in the dialog.</param>
    /// <param name="userName">An optional default username.</param>
    /// <param name="password">An optional default password.</param>
    /// <param name="saveCredential">Specifies whether the save checkbox should be displayed and its default state.</param>
    /// <returns>A <see cref="CredentialResult"/> if the user entered credentials; otherwise, <c>null</c>.</returns>
    [SupportedOSPlatform("windows6.0.6000")]
    public static unsafe CredentialResult? PromptForCredentials(IntPtr owner, string? messageText, string? captionText, string? userName, string? password, CredentialSaveOption saveCredential)
    {
        return PromptForCredentials(owner, messageText, captionText, userName, password, saveCredential, CredentialErrorCode.None);
    }

    /// <summary>Prompts the user for credentials using a Windows dialog.</summary>
    /// <param name="owner">The handle of the parent window.</param>
    /// <param name="messageText">The message text to display in the dialog.</param>
    /// <param name="captionText">The caption text to display in the dialog.</param>
    /// <param name="userName">An optional default username.</param>
    /// <param name="password">An optional default password.</param>
    /// <param name="saveCredential">Specifies whether the save checkbox should be displayed and its default state.</param>
    /// <param name="error">Specifies an error code to display an error message.</param>
    /// <returns>A <see cref="CredentialResult"/> if the user entered credentials; otherwise, <c>null</c>.</returns>
    [SupportedOSPlatform("windows6.0.6000")]
    public static unsafe CredentialResult? PromptForCredentials(IntPtr owner = default, string? messageText = null, string? captionText = null, string? userName = null, string? password = null, CredentialSaveOption saveCredential = CredentialSaveOption.Unselected, CredentialErrorCode error = CredentialErrorCode.None)
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
            var errorcode = error switch
            {
                CredentialErrorCode.LogonFailure => (uint)WIN32_ERROR.ERROR_LOGON_FAILURE,
                _ => 0u
            };

            var flags = CREDUIWIN_FLAGS.CREDUIWIN_GENERIC | CREDUIWIN_FLAGS.CREDUIWIN_ENUMERATE_CURRENT_USER;
            if (saveCredential != CredentialSaveOption.Hidden)
            {
                flags |= CREDUIWIN_FLAGS.CREDUIWIN_CHECKBOX;
            }

            // Prefill username
            GetInputBuffer(userName, password, out var inCredBuffer, out var inCredSize);

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

            if (result == 0 && GetCredentialsFromOutputBuffer(outCredBuffer, outCredBufferSize, out userName, out password, out var domain))
            {
                var credentialSaved = saveCredential is CredentialSaveOption.Hidden ? CredentialSaveOption.Hidden : (save ? CredentialSaveOption.Selected : CredentialSaveOption.Unselected);
                return new CredentialResult(userName, password, domain, credentialSaved);
            }

            return null;
        }
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private static unsafe void GetInputBuffer(string? user, string? password, out byte* inCredBuffer, out uint inCredSize)
    {
        if (!string.IsNullOrEmpty(user))
        {
            fixed (char* userPtr = user)
            fixed (char* passwordPtr = password ?? "")
            fixed (uint* inCredSizePtr = &inCredSize)
            {
                inCredSize = 1024;
                inCredBuffer = (byte*)Marshal.AllocCoTaskMem((int)inCredSize);
                if (PInvoke.CredPackAuthenticationBuffer(default, userPtr, passwordPtr, inCredBuffer, inCredSizePtr))
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
        Span<char> usernameBuf = new char[maxUserName];
        Span<char> passwordBuf = new char[maxDomain];
        Span<char> domainBuf = new char[maxPassword];
        try
        {
            fixed (char* usernamePtr = usernameBuf)
            fixed (char* passwordPtr = passwordBuf)
            fixed (char* domainPtr = domainBuf)
            {
                if (PInvoke.CredUnPackAuthenticationBuffer(default, outCredBuffer, outCredSize, new PWSTR(usernamePtr), &maxUserName, new PWSTR(domainPtr), &maxDomain, new PWSTR(passwordPtr), &maxPassword))
                {
                    userName = ToString(usernameBuf, maxUserName);
                    password = ToString(passwordBuf, maxPassword);
                    domain = ToString(domainBuf, maxDomain);

                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        var returnCode = PInvoke.CredUIParseUserName(userName, usernameBuf, domainBuf);
                        switch (returnCode)
                        {
                            case WIN32_ERROR.NO_ERROR:
                                userName = ToStringZero(usernameBuf);
                                domain = ToStringZero(domainBuf);
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
        }
        finally
        {
            //mimic SecureZeroMem function to make sure buffer is zeroed out. SecureZeroMem is not an exported function, neither is RtlSecureZeroMemory
            var zeroBytes = new byte[outCredSize];
            Marshal.Copy(zeroBytes, 0, (nint)outCredBuffer, (int)outCredSize);
            FreeCoTaskMem((nint)outCredBuffer);
        }

        static string ToString(ReadOnlySpan<char> buffer, uint length)
        {
            if (length == 0)
                return "";

            // Remove trailing \0
            Debug.Assert(buffer[(int)length] == '\0');
            return buffer.Slice(0, (int)length - 1).ToString();
        }

        static string ToStringZero(ReadOnlySpan<char> buffer)
        {
            var index = buffer.IndexOf('\0');
            return index >= 0 ? buffer.Slice(0, index).ToString() : buffer.ToString();
        }
    }

    private static void FreeCoTaskMem(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return;

        Marshal.FreeCoTaskMem(ptr);
    }
}
