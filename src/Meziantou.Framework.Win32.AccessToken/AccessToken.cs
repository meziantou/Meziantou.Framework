using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public sealed class AccessToken : IDisposable
{
    private SafeFileHandle _token;

    private AccessToken(SafeFileHandle token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public bool IsRestricted()
    {
        return PInvoke.IsTokenRestricted(_token);
    }

    public unsafe TokenType GetTokenType()
    {
        TokenType result;
        if (!PInvoke.GetTokenInformation(_token, TOKEN_INFORMATION_CLASS.TokenType, &result, (uint)IntPtr.Size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return result;
    }

    public unsafe TokenElevationType GetElevationType()
    {
        TokenElevationType result;
        if (!PInvoke.GetTokenInformation(_token, TOKEN_INFORMATION_CLASS.TokenElevationType, &result, (uint)IntPtr.Size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return result;
    }

    public bool IsElevated()
    {
        return GetTokenInformation<TOKEN_ELEVATION>(TOKEN_INFORMATION_CLASS.TokenElevation).TokenIsElevated != 0u;
    }

    public AccessToken? GetLinkedToken() => GetTokenInformation<TOKEN_LINKED_TOKEN, AccessToken?>(
            TOKEN_INFORMATION_CLASS.TokenLinkedToken,
            linkedToken =>
            {
                if (linkedToken.LinkedToken == IntPtr.Zero)
                    return null;

                return new AccessToken(new SafeFileHandle(linkedToken.LinkedToken, ownsHandle: true));
            });

    public TokenEntry? GetMandatoryIntegrityLevel()
    {
        return GetTokenInformation<TOKEN_MANDATORY_LABEL, TokenEntry>(
            TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
            mandatoryLabel => new TokenEntry(new SecurityIdentifier(mandatoryLabel.Label.Sid)));
    }

    public SecurityIdentifier? GetOwner()
    {
        return GetTokenInformation<TOKEN_OWNER, SecurityIdentifier>(
            TOKEN_INFORMATION_CLASS.TokenOwner,
            owner => new SecurityIdentifier(owner.Owner));
    }

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

        fixed (char* name = new char[luidNameLen])
        {
            if (PInvoke.LookupPrivilegeName(lpSystemName: null, in luid, name, ref luidNameLen))
                return new string(name);
        }

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
        if (!PInvoke.GetTokenInformation(_token, type, null, 0u, out var dwLength))
        {
            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode)
            {
                case (int)WIN32_ERROR.ERROR_BAD_LENGTH:
                // special case for TokenSessionId. Falling through
                case (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER:
                    var handle = Marshal.AllocHGlobal((int)dwLength);
                    try
                    {
                        if (!PInvoke.GetTokenInformation(_token, type, (void*)handle, dwLength, out _))
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

    public void EnablePrivilege(string privilegeName)
    {
        if (privilegeName is null)
            throw new ArgumentNullException(nameof(privilegeName));

        AdjustPrivilege(privilegeName, PrivilegeOperation.Enable);
    }

    public void DisablePrivilege(string privilegeName)
    {
        if (privilegeName is null)
            throw new ArgumentNullException(nameof(privilegeName));

        AdjustPrivilege(privilegeName, PrivilegeOperation.Disable);
    }

    public unsafe void DisableAllPrivileges()
    {
        uint returnSize = 0;
        if (!PInvoke.AdjustTokenPrivileges(_token, DisableAllPrivileges: true, NewState: null, BufferLength: 0, PreviousState: null, &returnSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void RemovePrivilege(string privilegeName)
    {
        if (privilegeName is null)
            throw new ArgumentNullException(nameof(privilegeName));

        AdjustPrivilege(privilegeName, PrivilegeOperation.Remove);
    }

    [SuppressMessage("Usage", "MA0099:Use Explicit enum value instead of 0", Justification = "The constant doesn't exist")]
    private unsafe void AdjustPrivilege(string privilegeName, PrivilegeOperation operation)
    {
        if (!PInvoke.LookupPrivilegeValue(lpSystemName: null, privilegeName, out var luid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = (ReadOnlySpan<LUID_AND_ATTRIBUTES>)
            [
                new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = operation switch
                    {
                        PrivilegeOperation.Enable => TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,
                        PrivilegeOperation.Remove => TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_REMOVED,
                        _ => 0,
                    },
                },
            ],
        };

        uint returnSize = 0;
        if (!PInvoke.AdjustTokenPrivileges(_token, DisableAllPrivileges: false, tp, 0, PreviousState: null, &returnSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public AccessToken Duplicate(SecurityImpersonationLevel impersonationLevel)
    {
        if (!PInvoke.DuplicateToken(_token, (SECURITY_IMPERSONATION_LEVEL)impersonationLevel, out var duplicateTokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(duplicateTokenHandle);
    }

    public void Dispose()
    {
        _token.Dispose();
        _token = null;
    }

    public static AccessToken OpenCurrentProcessToken(TokenAccessLevels accessLevels)
    {
        using var currentProcess = PInvoke.GetCurrentProcess_SafeHandle();
        if (!PInvoke.OpenProcessToken(currentProcess, (TOKEN_ACCESS_MASK)accessLevels, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(tokenHandle);
    }

    public static AccessToken OpenProcessToken(Process process, TokenAccessLevels accessLevels)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));

        if (!PInvoke.OpenProcessToken(process.SafeHandle, (TOKEN_ACCESS_MASK)accessLevels, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new AccessToken(tokenHandle);
    }

    public static bool IsLimitedToken()
    {
        using var token = OpenCurrentProcessToken(TokenAccessLevels.Query);
        return token.GetElevationType() == TokenElevationType.Limited;
    }

    private enum PrivilegeOperation
    {
        Enable,
        Disable,
        Remove,
    }
}
