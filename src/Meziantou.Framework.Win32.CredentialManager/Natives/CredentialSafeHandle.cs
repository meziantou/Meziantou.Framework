using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32.Natives
{
    internal sealed class CredentialSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CredentialSafeHandle()
            : base(ownsHandle: true)
        {
        }

        public CREDENTIAL GetCredential()
        {
            if (!IsInvalid)
            {
                return Marshal.PtrToStructure<CREDENTIAL>(handle);
            }

            throw new InvalidOperationException("Invalid CriticalHandle!");
        }

        protected override bool ReleaseHandle()
        {
            return Advapi32.CredFree(handle);
        }
    }
}
