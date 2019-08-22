using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32
{
    internal sealed class AmsiContextSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public AmsiContextSafeHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            Amsi.AmsiUninitialize(handle);
            return true;
        }
    }
}
