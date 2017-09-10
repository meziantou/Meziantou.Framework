using System;
using System.Runtime.InteropServices;
using Meziantou.Framework.Security.Native;

namespace Meziantou.Framework.Security
{
    internal sealed class CriticalCredentialHandle : CriticalHandleZeroOrMinusOneIsInvalid
    {
        public CriticalCredentialHandle(IntPtr preexistingHandle)
        {
            SetHandle(preexistingHandle);
        }

        public CREDENTIAL GetCredential()
        {
            if (!IsInvalid)
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(handle);
                return credential;
            }

            throw new InvalidOperationException("Invalid CriticalHandle!");
        }

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                Advapi32.CredFree(handle);
                SetHandleAsInvalid();
                return true;
            }

            return false;
        }
    }
}