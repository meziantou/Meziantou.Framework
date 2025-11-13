using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a Windows access token that identifies a security context for a process or thread.</summary>
/// <example>
/// <code>
/// // Open the current process token
/// using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
/// 
/// // Check if the process is elevated
/// bool isElevated = token.IsElevated();
/// 
/// // Get token information
/// Console.WriteLine($"Token Type: {token.GetTokenType()}");
/// Console.WriteLine($"Elevation Type: {token.GetElevationType()}");
/// Console.WriteLine($"Owner: {token.GetOwner()}");
/// 
/// // Enumerate groups
/// foreach (var group in token.EnumerateGroups())
/// {
///     Console.WriteLine($"Group: {group.Sid} ({group.Attributes})");
/// }
/// 
/// // Enumerate privileges
/// foreach (var privilege in token.EnumeratePrivileges())
/// {
///     Console.WriteLine($"Privilege: {privilege.Name} ({privilege.Attributes})");
/// }
/// 
/// // Enable a privilege
/// token.EnablePrivilege(Privileges.SE_DEBUG_NAME);
/// </code>
/// </example>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class AccessToken : IDisposable
{
    private SafeFileHandle _token;

    private AccessToken(SafeFileHandle token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    /// <summary>Determines whether the token is restricted.</summary>
    /// <returns><see langword="true"/> if the token is restricted; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// A restricted token has restricted security identifiers (SIDs) added to it. These restrictions
    /// limit the resources that can be accessed with the token.
    /// </remarks>
    public bool IsRestricted()
    {
        return PInvoke.IsTokenRestricted(_token);
    }

    /// <summary>Gets the type of the token (primary or impersonation).</summary>
    /// <returns>A <see cref="TokenType"/> value indicating the token type.</returns>
    public unsafe TokenType GetTokenType()
    {
        TokenType result;
        uint returnedLength;
        using var safeHandleValue = new SafeHandleValue(_token);
        if (!PInvoke.GetTokenInformation((HANDLE)safeHandleValue.Value, TOKEN_INFORMATION_CLASS.TokenType, &result, (uint)IntPtr.Size, &returnedLength))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return result;
    }

    /// <summary>Gets the elevation type of the token.</summary>
    /// <returns>A <see cref="TokenElevationType"/> value indicating the elevation status.</returns>
    /// <remarks>
    /// The elevation type indicates whether the token is elevated, limited, or default. This is useful
    /// for User Account Control (UAC) scenarios.
    /// </remarks>
    public unsafe TokenElevationType GetElevationType()
    {
        TokenElevationType result;
        uint returnedLength;
        using var safeHandleValue = new SafeHandleValue(_token);
        if (!PInvoke.GetTokenInformation((HANDLE)safeHandleValue.Value, TOKEN_INFORMATION_CLASS.TokenElevationType, &result, (uint)IntPtr.Size, &returnedLength))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return result;
    }

    /// <summary>Determines whether the token is elevated (running with administrative privileges).</summary>
    /// <returns><see langword="true"/> if the token is elevated; otherwise, <see langword="false"/>.</returns>
    public bool IsElevated()
    {
        return GetTokenInformation<TOKEN_ELEVATION>(TOKEN_INFORMATION_CLASS.TokenElevation).TokenIsElevated != 0u;
    }

    /// <summary>Gets the linked token associated with this token.</summary>
    /// <returns>The linked <see cref="AccessToken"/>, or <see langword="null"/> if no linked token exists.</returns>
    /// <remarks>
    /// In UAC scenarios, an administrator account has two tokens: a limited token for normal operations
    /// and an elevated token for administrative tasks. This method retrieves the linked token.
    /// </remarks>
    public AccessToken? GetLinkedToken() => GetTokenInformation<TOKEN_LINKED_TOKEN, AccessToken?>(
            TOKEN_INFORMATION_CLASS.TokenLinkedToken,
            linkedToken =>
            {
                if (linkedToken.LinkedToken == IntPtr.Zero)
                    return null;

                return new AccessToken(new SafeFileHandle(linkedToken.LinkedToken, ownsHandle: true));
            });

    /// <summary>Gets the mandatory integrity level of the token.</summary>
    /// <returns>A <see cref="TokenEntry"/> containing the integrity level SID, or <see langword="null"/> if not available.</returns>
    /// <remarks>The mandatory integrity level (e.g., Low, Medium, High, System) determines what resources the token can access.</remarks>
    public TokenEntry? GetMandatoryIntegrityLevel()
    {
        return GetTokenInformation<TOKEN_MANDATORY_LABEL, TokenEntry>(
            TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
            mandatoryLabel => new TokenEntry(new SecurityIdentifier(mandatoryLabel.Label.Sid)));
    }

    /// <summary>Gets the owner security identifier (SID) of the token.</summary>
    /// <returns>The <see cref="SecurityIdentifier"/> of the token owner, or <see langword="null"/> if not available.</returns>
    public SecurityIdentifier? GetOwner()
    {
        return GetTokenInformation<TOKEN_OWNER, SecurityIdentifier>(
            TOKEN_INFORMATION_CLASS.TokenOwner,
            owner => new SecurityIdentifier(owner.Owner));
    }

    /// <summary>Enumerates all groups (security identifiers) associated with the token.</summary>
    /// <returns>A collection of <see cref="TokenGroupEntry"/> objects representing the groups, or <see langword="null"/> if not available.</returns>
    public IEnumerable<TokenGroupEntry>? EnumerateGroups()
    {
        return GetTokenInformation<TOKEN_GROUPS, IReadOnlyList<TokenGroupEntry>>(
            TOKEN_INFORMATION_CLASS.TokenGroups,
            (handle, groups) =>
            {
                var list = new TokenGroupEntry[groups.GroupCount];
                var index = 0;
                foreach (var group in ReadArray<TOKEN_GROUPS, SID_AND_ATTRIBUTES>(handle, nameof(TOKEN_GROUPS.Groups), groups.GroupCount))
                {
                    list[index] = new TokenGroupEntry(new SecurityIdentifier(group.Sid), (GroupSidAttributes)group.Attributes);
                    index++;
                }

                return list;
            });
    }

    /// <summary>Enumerates the restricted SIDs in the token.</summary>
    /// <returns>A collection of <see cref="TokenGroupEntry"/> objects representing the restricted SIDs, or <see langword="null"/> if not available.</returns>
    /// <remarks>Restricted SIDs are used to limit the token's access to resources beyond the standard access checks.</remarks>
    public IEnumerable<TokenGroupEntry>? EnumerateRestrictedSid()
    {
        return GetTokenInformation<TOKEN_GROUPS, IReadOnlyList<TokenGroupEntry>>(
            TOKEN_INFORMATION_CLASS.TokenRestrictedSids,
            (handle, groups) =>
            {
                var list = new TokenGroupEntry[groups.GroupCount];
                var index = 0;
                foreach (var group in ReadArray<TOKEN_GROUPS, SID_AND_ATTRIBUTES>(handle, nameof(TOKEN_GROUPS.Groups), groups.GroupCount))
                {
                    list[index] = new TokenGroupEntry(new SecurityIdentifier(group.Sid), (GroupSidAttributes)group.Attributes);
                    index++;
                }

                return list;
            });
    }

    /// <summary>Enumerates all privileges held by the token.</summary>
    /// <returns>A collection of <see cref="TokenPrivilegeEntry"/> objects representing the privileges, or <see langword="null"/> if not available.</returns>
    public IEnumerable<TokenPrivilegeEntry>? EnumeratePrivileges()
    {
        return GetTokenInformation<TOKEN_PRIVILEGES, IReadOnlyList<TokenPrivilegeEntry>>(
            TOKEN_INFORMATION_CLASS.TokenPrivileges,
            (handle, privileges) =>
            {
                var list = new TokenPrivilegeEntry[privileges.PrivilegeCount];
                var index = 0;
                foreach (var privilege in ReadArray<TOKEN_PRIVILEGES, LUID_AND_ATTRIBUTES>(handle, nameof(TOKEN_PRIVILEGES.Privileges), privileges.PrivilegeCount))
                {
                    var name = LookupPrivilegeName(privilege.Luid);
                    list[index] = new TokenPrivilegeEntry(name, (PrivilegeAttribute)privilege.Attributes);
                    index++;
                }

                return list;
            });
    }

    private static unsafe string LookupPrivilegeName(LUID luid)
    {
        var luidNameLen = 0u;
        PInvoke.LookupPrivilegeName(lpSystemName: null, in luid, lpName: null, ref luidNameLen);

        Span<char> name = new char[luidNameLen];
        if (PInvoke.LookupPrivilegeName(lpSystemName: null, in luid, name, ref luidNameLen))
            return new string(name);

        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static IEnumerable<TItem> ReadArray<TContainer, TItem>(IntPtr handle, string fieldName, uint count)
    {
        var offset = Marshal.OffsetOf<TContainer>(fieldName);
        var size = Marshal.SizeOf<TItem>();
        var basePtr = new IntPtr(handle.ToInt64() + offset.ToInt64());
        for (var i = 0; i < count; i++)
        {
            yield return Marshal.PtrToStructure<TItem>(basePtr + (i * size))!;
        }
    }

    private T GetTokenInformation<T>(TOKEN_INFORMATION_CLASS type) where T : unmanaged
    {
        return GetTokenInformation<T, T>(type, Identity);

        static T Identity(T arg) => arg;
    }

    private TResult? GetTokenInformation<T, TResult>(TOKEN_INFORMATION_CLASS type, Func<T, TResult> func)
        where T : unmanaged
    {
        return GetTokenInformation<T, TResult>(type, (_, arg) => func(arg))!;
    }

    private unsafe TResult? GetTokenInformation<T, TResult>(TOKEN_INFORMATION_CLASS type, Func<IntPtr, T, TResult> func)
        where T : unmanaged
    {
        uint returnedLength;
        using var safeHandleValue = new SafeHandleValue(_token);
        if (!PInvoke.GetTokenInformation((HANDLE)safeHandleValue.Value, type, TokenInformation: null, 0u, &returnedLength))
        {
            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode)
            {
                case (int)WIN32_ERROR.ERROR_BAD_LENGTH:
                // special case for TokenSessionId. Falling through
                case (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER:
                    var handle = Marshal.AllocHGlobal((int)returnedLength);
                    try
                    {
                        if (!PInvoke.GetTokenInformation((HANDLE)safeHandleValue.Value, type, (void*)handle, returnedLength, &returnedLength))
                            throw new Win32Exception(Marshal.GetLastWin32Error());

                        var s = Marshal.PtrToStructure<T>(handle);
                        return func(handle, s);
                    }
                    finally
                    {
                        if (handle != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(handle);
                        }
                    }

                case (int)WIN32_ERROR.ERROR_INVALID_HANDLE:
                    throw new Win32Exception(errorCode, "Invalid impersonation token");

                default:
                    throw new Win32Exception(errorCode);
            }
        }

        return default!;
    }

    /// <summary>Enables a privilege for this token.</summary>
    /// <param name="privilegeName">The name of the privilege to enable (e.g., <see cref="Privileges.SE_DEBUG_NAME"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="privilegeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The privilege could not be enabled.</exception>
    public void EnablePrivilege(string privilegeName)
    {
        ArgumentNullException.ThrowIfNull(privilegeName);

        AdjustPrivilege(privilegeName, PrivilegeOperation.Enable);
    }

    /// <summary>Disables a privilege for this token.</summary>
    /// <param name="privilegeName">The name of the privilege to disable (e.g., <see cref="Privileges.SE_DEBUG_NAME"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="privilegeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The privilege could not be disabled.</exception>
    public void DisablePrivilege(string privilegeName)
    {
        ArgumentNullException.ThrowIfNull(privilegeName);

        AdjustPrivilege(privilegeName, PrivilegeOperation.Disable);
    }

    /// <summary>Disables all privileges for this token.</summary>
    /// <exception cref="Win32Exception">The operation failed.</exception>
    public unsafe void DisableAllPrivileges()
    {
        uint returnSize = 0;
        using var safeHandleValue = new SafeHandleValue(_token);
        if (!PInvoke.AdjustTokenPrivileges((HANDLE)safeHandleValue.Value, DisableAllPrivileges: true, NewState: null, BufferLength: 0, PreviousState: null, &returnSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Removes a privilege from this token.</summary>
    /// <param name="privilegeName">The name of the privilege to remove (e.g., <see cref="Privileges.SE_DEBUG_NAME"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="privilegeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The privilege could not be removed.</exception>
    public void RemovePrivilege(string privilegeName)
    {
        ArgumentNullException.ThrowIfNull(privilegeName);

        AdjustPrivilege(privilegeName, PrivilegeOperation.Remove);
    }

    [SuppressMessage("Usage", "MA0099:Use Explicit enum value instead of 0", Justification = "The constant doesn't exist")]
    private unsafe void AdjustPrivilege(string privilegeName, PrivilegeOperation operation)
    {
        if (!PInvoke.LookupPrivilegeValue(lpSystemName: null, privilegeName, out var luid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var privileges = new VariableLengthInlineArray<LUID_AND_ATTRIBUTES>();
        privileges[0] = new LUID_AND_ATTRIBUTES
        {
            Luid = luid,
            Attributes = operation switch
            {
                PrivilegeOperation.Enable => TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,
                PrivilegeOperation.Remove => TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_REMOVED,
                _ => 0,
            },
        };
        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = privileges,
        };

        uint returnSize = 0;
        using var safeHandleValue = new SafeHandleValue(_token);
        if (!PInvoke.AdjustTokenPrivileges((HANDLE)safeHandleValue.Value, DisableAllPrivileges: false, &tp, 0, PreviousState: null, &returnSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Creates a duplicate of this token with the specified impersonation level.</summary>
    /// <param name="impersonationLevel">The impersonation level for the duplicate token.</param>
    /// <returns>A new <see cref="AccessToken"/> representing the duplicate.</returns>
    /// <exception cref="Win32Exception">The token could not be duplicated.</exception>
    public AccessToken Duplicate(SecurityImpersonationLevel impersonationLevel)
    {
        if (!PInvoke.DuplicateToken(_token, (SECURITY_IMPERSONATION_LEVEL)impersonationLevel, out var duplicateTokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(duplicateTokenHandle);
    }

    /// <summary>Releases the unmanaged resources used by the <see cref="AccessToken"/>.</summary>
    public void Dispose()
    {
        _token.Dispose();
        _token = null;
    }

    /// <summary>Opens the access token associated with the current process.</summary>
    /// <param name="accessLevels">The access levels required for the token.</param>
    /// <returns>An <see cref="AccessToken"/> representing the current process token.</returns>
    /// <exception cref="Win32Exception">The token could not be opened.</exception>
    public static AccessToken OpenCurrentProcessToken(TokenAccessLevels accessLevels)
    {
        using var currentProcess = PInvoke.GetCurrentProcess_SafeHandle();
        if (!PInvoke.OpenProcessToken(currentProcess, (TOKEN_ACCESS_MASK)accessLevels, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(tokenHandle);
    }

    /// <summary>Opens the access token associated with the specified process.</summary>
    /// <param name="process">The process whose token to open.</param>
    /// <param name="accessLevels">The access levels required for the token.</param>
    /// <returns>An <see cref="AccessToken"/> representing the process token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="process"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The token could not be opened.</exception>
    public static AccessToken OpenProcessToken(Process process, TokenAccessLevels accessLevels)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!PInvoke.OpenProcessToken(process.SafeHandle, (TOKEN_ACCESS_MASK)accessLevels, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(tokenHandle);
    }

    /// <summary>Determines whether the current process is running with a limited token.</summary>
    /// <returns><see langword="true"/> if the current process is running with a limited token; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// A limited token is created when User Account Control (UAC) is enabled and an administrator
    /// logs in. The limited token has fewer privileges than the full administrative token.
    /// </remarks>
    public static bool IsLimitedToken()
    {
        using var token = OpenCurrentProcessToken(TokenAccessLevels.Query);
        return token.GetElevationType() is TokenElevationType.Limited;
    }

    private enum PrivilegeOperation
    {
        Enable,
        Disable,
        Remove,
    }
}
