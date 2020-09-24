using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    [SupportedOSPlatform("windows")]
    public sealed class AccessToken : IDisposable
    {
        private IntPtr _token;

        private AccessToken(IntPtr token)
        {
            if (token == IntPtr.Zero)
                throw new ArgumentNullException(nameof(token));

            _token = token;
        }

        public bool IsRestricted()
        {
            return NativeMethods.IsTokenRestricted(_token);
        }

        public TokenType GetTokenType()
        {
            if (!NativeMethods.GetTokenInformation(_token, TokenInformationClass.TokenType, out TokenType result, IntPtr.Size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return result;
        }

        public TokenElevationType GetElevationType()
        {
            if (!NativeMethods.GetTokenInformation(_token, TokenInformationClass.TokenElevationType, out TokenElevationType result, IntPtr.Size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return result;
        }

        public bool IsElevated()
        {
            return GetTokenInformation<NativeMethods.TOKEN_ELEVATION>(TokenInformationClass.TokenElevation).TokenIsElevated;
        }

        public AccessToken? GetLinkedToken() => GetTokenInformation<NativeMethods.TOKEN_LINKED_TOKEN, AccessToken?>(
                TokenInformationClass.TokenLinkedToken,
                linkedToken =>
                {
                    if (linkedToken.LinkedToken == IntPtr.Zero)
                        return null;

                    return new AccessToken(linkedToken.LinkedToken);
                });

        public TokenEntry? GetMandatoryIntegrityLevel()
        {
            return GetTokenInformation<NativeMethods.TOKEN_MANDATORY_LABEL, TokenEntry>(
                TokenInformationClass.TokenIntegrityLevel,
                mandatoryLabel => new TokenEntry(new SecurityIdentifier(mandatoryLabel.Label.Sid)));
        }

        public SecurityIdentifier? GetOwner()
        {
            return GetTokenInformation<NativeMethods.TOKEN_OWNER, SecurityIdentifier>(
                TokenInformationClass.TokenOwner,
                owner => new SecurityIdentifier(owner.Owner));
        }

        public IEnumerable<TokenGroupEntry>? EnumerateGroups()
        {
            return GetTokenInformation<NativeMethods.TOKEN_GROUPS, IReadOnlyList<TokenGroupEntry>>(
                TokenInformationClass.TokenGroups,
                (handle, groups) =>
                {
                    var list = new List<TokenGroupEntry>(groups.GroupCount);
                    foreach (var group in ReadArray<NativeMethods.TOKEN_GROUPS, NativeMethods.SID_AND_ATTRIBUTES>(handle, nameof(NativeMethods.TOKEN_GROUPS.Groups), groups.GroupCount))
                    {
                        list.Add(new TokenGroupEntry(new SecurityIdentifier(group.Sid), (GroupSidAttributes)group.Attributes));
                    }

                    return list;
                });
        }

        public IEnumerable<TokenGroupEntry>? EnumerateRestrictedSid()
        {
            return GetTokenInformation<NativeMethods.TOKEN_GROUPS, IReadOnlyList<TokenGroupEntry>>(
                TokenInformationClass.TokenRestrictedSids,
                (handle, groups) =>
                {
                    var list = new List<TokenGroupEntry>(groups.GroupCount);
                    foreach (var group in ReadArray<NativeMethods.TOKEN_GROUPS, NativeMethods.SID_AND_ATTRIBUTES>(handle, nameof(NativeMethods.TOKEN_GROUPS.Groups), groups.GroupCount))
                    {
                        list.Add(new TokenGroupEntry(new SecurityIdentifier(group.Sid), (GroupSidAttributes)group.Attributes));
                    }

                    return list;
                });
        }

        public IEnumerable<TokenPrivilegeEntry>? EnumeratePrivileges()
        {
            return GetTokenInformation<NativeMethods.TOKEN_PRIVILEGES, IReadOnlyList<TokenPrivilegeEntry>>(
                TokenInformationClass.TokenPrivileges,
                (handle, privileges) =>
                {
                    var list = new List<TokenPrivilegeEntry>(privileges.PrivilegeCount);
                    foreach (var privilege in ReadArray<NativeMethods.TOKEN_PRIVILEGES, NativeMethods.LUID_AND_ATTRIBUTES>(handle, nameof(NativeMethods.TOKEN_PRIVILEGES.Privileges), privileges.PrivilegeCount))
                    {
                        var name = NativeMethods.LookupPrivilegeName(privilege.Luid);

                        list.Add(new TokenPrivilegeEntry(name, (PrivilegeAttribute)privilege.Attributes));
                    }

                    return list;
                });
        }

        private static IEnumerable<TItem> ReadArray<TContainer, TItem>(IntPtr handle, string fieldName, int count)
        {
            var offset = Marshal.OffsetOf<TContainer>(fieldName);
            var size = Marshal.SizeOf<TItem>();
            var basePtr = new IntPtr(handle.ToInt64() + offset.ToInt64());
            for (var i = 0; i < count; i++)
            {
                yield return Marshal.PtrToStructure<TItem>(basePtr + (i * size))!;
            }
        }

        private T GetTokenInformation<T>(TokenInformationClass type) where T : struct
        {
            return GetTokenInformation<T, T>(type, Identity);

            static T Identity(T arg) => arg;
        }

        private TResult? GetTokenInformation<T, TResult>(TokenInformationClass type, Func<T, TResult> func)
            where T : struct
        {
            return GetTokenInformation<T, TResult>(type, (_, arg) => func(arg))!;
        }

        private TResult? GetTokenInformation<T, TResult>(TokenInformationClass type, Func<IntPtr, T, TResult> func)
            where T : struct
        {
            if (!NativeMethods.GetTokenInformation(_token, type, IntPtr.Zero, 0, out var dwLength))
            {
                var errorCode = Marshal.GetLastWin32Error();
                switch (errorCode)
                {
                    case NativeMethods.ERROR_BAD_LENGTH:
                    // special case for TokenSessionId. Falling through
                    case NativeMethods.ERROR_INSUFFICIENT_BUFFER:
                        var handle = Marshal.AllocHGlobal((int)dwLength);
                        try
                        {
                            if (!NativeMethods.GetTokenInformation(_token, type, handle, dwLength, out _))
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

                    case NativeMethods.ERROR_INVALID_HANDLE:
                        throw new Win32Exception(errorCode, "Invalid impersonation token");

                    default:
                        throw new Win32Exception(errorCode);
                }
            }

            return default!;
        }

        public void EnablePrivilege(string privilegeName)
        {
            if (privilegeName == null)
                throw new ArgumentNullException(nameof(privilegeName));

            AdjustPrivilege(privilegeName, PrivilegeOperation.Enable);
        }

        public void DisablePrivilege(string privilegeName)
        {
            if (privilegeName == null)
                throw new ArgumentNullException(nameof(privilegeName));

            AdjustPrivilege(privilegeName, PrivilegeOperation.Disable);
        }

        public void DisableAllPrivileges()
        {
            uint returnSize = 0;
            if (!NativeMethods.AdjustTokenPrivileges(_token, disableAllPrivileges: true, IntPtr.Zero, 0, IntPtr.Zero, ref returnSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void RemovePrivilege(string privilegeName)
        {
            if (privilegeName == null)
                throw new ArgumentNullException(nameof(privilegeName));

            AdjustPrivilege(privilegeName, PrivilegeOperation.Remove);
        }

        private void AdjustPrivilege(string privilegeName, PrivilegeOperation operation)
        {
            if (!NativeMethods.LookupPrivilegeValueW(lpSystemName: null, privilegeName, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new NativeMethods.LUID_AND_ATTRIBUTES[1]
                {
                    new NativeMethods.LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                    },
                },
            };

            switch (operation)
            {
                case PrivilegeOperation.Enable:
                    tp.Privileges[0].Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;
                    break;
                case PrivilegeOperation.Disable:
                    tp.Privileges[0].Attributes = 0;
                    break;
                case PrivilegeOperation.Remove:
                    tp.Privileges[0].Attributes = NativeMethods.SE_PRIVILEGE_REMOVED;
                    break;
            }

            uint returnSize = 0;
            if (!NativeMethods.AdjustTokenPrivileges(_token, disableAllPrivileges: false, ref tp, 0, IntPtr.Zero, ref returnSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public AccessToken Duplicate(SecurityImpersonationLevel impersonationLevel)
        {
            if (!NativeMethods.DuplicateToken(_token, impersonationLevel, out var duplicateTokenHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return new AccessToken(duplicateTokenHandle);
        }

        public void Dispose()
        {
            if (_token != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_token);
                _token = IntPtr.Zero;
            }
        }

        public static AccessToken OpenCurrentProcessToken(TokenAccessLevels accessLevels)
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), accessLevels, out var tokenHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return new AccessToken(tokenHandle);
        }

        public static AccessToken OpenProcessToken(Process process, TokenAccessLevels accessLevels)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (!NativeMethods.OpenProcessToken(process.Handle, accessLevels, out var tokenHandle))
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
}
