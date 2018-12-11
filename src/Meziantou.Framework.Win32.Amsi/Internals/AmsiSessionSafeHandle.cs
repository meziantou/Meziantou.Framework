using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32
{
    internal class AmsiSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal AmsiContextSafeHandle Context { get; set; }

        public AmsiSessionSafeHandle()
            : base(ownsHandle: true)
        {
        }

        public override bool IsInvalid => Context.IsInvalid || base.IsInvalid;

        protected override bool ReleaseHandle()
        {
            Amsi.AmsiCloseSession(Context, handle);
            return true;
        }
    }
}
