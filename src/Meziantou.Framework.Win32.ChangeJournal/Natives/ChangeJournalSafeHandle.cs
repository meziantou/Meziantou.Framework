using System;
using System.Runtime.InteropServices;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32.Natives
{
    internal class ChangeJournalSafeHandle : SafeHandle
    {
        public IntPtr Handle { get; }

        public ChangeJournalSafeHandle(IntPtr handle)
            : base(IntPtr.Zero, true)
        {
            Handle = handle;
        }

        public override bool IsInvalid => Handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                return Win32Methods.CloseHandle(Handle);
            }

            return false;
        }
    }
}