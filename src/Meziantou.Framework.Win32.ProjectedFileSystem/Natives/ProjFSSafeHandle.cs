﻿using System;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    internal sealed class ProjFSSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ProjFSSafeHandle()
            : base(ownsHandle: true)
        {
        }

        public ProjFSSafeHandle(IntPtr handle, bool ownHandle)
            : base(ownsHandle: ownHandle)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            NativeMethods.PrjStopVirtualizing(handle);
            return true;
        }
    }
}
