﻿using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32
{
    internal sealed class AmsiSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal AmsiContextSafeHandle? Context { get; set; }

        public AmsiSessionSafeHandle()
            : base(ownsHandle: true)
        {
        }

        public override bool IsInvalid => Context == null || Context.IsInvalid || base.IsInvalid;

        protected override bool ReleaseHandle()
        {
            Debug.Assert(Context != null);
            Amsi.AmsiCloseSession(Context, handle);
            return true;
        }
    }
}
