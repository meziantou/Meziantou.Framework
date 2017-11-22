using Meziantou.Framework.Win32.Natives;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Win32
{
    public class SecurityIdentifier
    {
        private const byte MaxSubAuthorities = 15;
        private const int MaxBinaryLength = 1 + 1 + 6 + MaxSubAuthorities * 4; // 4 bytes for each subauth

        private readonly IntPtr _sid;

        internal SecurityIdentifier(IntPtr sid)
        {
            if (sid == IntPtr.Zero)
                throw new ArgumentNullException(nameof(sid));

            _sid = sid;

            var name = LookupName(sid);
            Domain = name.domain;
            Name = name.name;
            Sid = ConvertSidToStringSid(sid);
        }

        public string Domain { get; }
        public string Name { get; }
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
            if (resultSid == IntPtr.Zero)
                throw new OutOfMemoryException();

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
            if (NativeMethods.ConvertSidToStringSid(sid, out string result))
                return result;

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private static (string domain, string name) LookupName(IntPtr sid)
        {
            var userNameLen = 256;
            var domainNameLen = 256;
            var bufUserName = new StringBuilder(userNameLen);
            var bufDomainName = new StringBuilder(domainNameLen);
            var sidNameUse = 0;

            if (NativeMethods.LookupAccountSid(null, sid, bufUserName, ref userNameLen, bufDomainName, ref domainNameLen, ref sidNameUse) != 0)
            {
                return (bufDomainName.ToString(), bufUserName.ToString());
            }

            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_NONE_MAPPED)
                return (null, null);

            throw new Win32Exception(error);
        }
    }
}
