using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    [SupportedOSPlatform("windows")]
    public sealed class SecurityIdentifier : IEquatable<SecurityIdentifier?>
    {
        private const byte MaxSubAuthorities = 15;
        private const int MaxBinaryLength = 1 + 1 + 6 + (MaxSubAuthorities * 4); // 4 bytes for each subauth

        internal SecurityIdentifier(IntPtr sid)
        {
            if (sid == IntPtr.Zero)
                throw new ArgumentNullException(nameof(sid));

            LookupName(sid, out var domain, out var name);
            Domain = domain;
            Name = name;
            Sid = ConvertSidToStringSid(sid);
        }

        public string? Domain { get; }
        public string? Name { get; }
        public string Sid { get; }

        public string FullName => Domain + "\\" + Name;

        public override string ToString()
        {
            if (Name == null)
                return Sid;

            return FullName;
        }

        public static SecurityIdentifier FromWellKnown(WellKnownSidType type)
        {
            uint size = MaxBinaryLength * sizeof(byte);
            var resultSid = Marshal.AllocHGlobal((int)size);

            try
            {
                if (!NativeMethods.CreateWellKnownSid((int)type, IntPtr.Zero, resultSid, ref size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return new SecurityIdentifier(resultSid);
            }
            finally
            {
                if (resultSid != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(resultSid);
                }
            }
        }

        private static string ConvertSidToStringSid(IntPtr sid)
        {
            if (NativeMethods.ConvertSidToStringSidW(sid, out var result))
                return result;

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void LookupName(IntPtr sid, out string? domain, out string? name)
        {
            var userNameLen = 256;
            var domainNameLen = 256;
            var bufUserName = new StringBuilder(userNameLen);
            var bufDomainName = new StringBuilder(domainNameLen);
            var sidNameUse = 0;

            if (NativeMethods.LookupAccountSidW(systemName: null, sid, bufUserName, ref userNameLen, bufDomainName, ref domainNameLen, ref sidNameUse) != 0)
            {
                domain = bufDomainName.ToString();
                name = bufUserName.ToString();
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_NONE_MAPPED)
            {
                domain = default;
                name = default;
                return;
            }

            throw new Win32Exception(error);
        }

        public bool Equals(SecurityIdentifier? other)
        {
            return other != null && Sid == other.Sid;
        }

        public override bool Equals(object? obj) => Equals(obj as SecurityIdentifier);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sid);

        public static bool operator ==(SecurityIdentifier? left, SecurityIdentifier? right) => EqualityComparer<SecurityIdentifier>.Default.Equals(left, right);

        public static bool operator !=(SecurityIdentifier? left, SecurityIdentifier? right) => !(left == right);
    }
}
