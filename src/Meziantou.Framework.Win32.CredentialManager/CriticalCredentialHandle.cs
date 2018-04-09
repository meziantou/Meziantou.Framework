using System;
using System.Runtime.InteropServices;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
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
                return Marshal.PtrToStructure<CREDENTIAL>(handle);
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