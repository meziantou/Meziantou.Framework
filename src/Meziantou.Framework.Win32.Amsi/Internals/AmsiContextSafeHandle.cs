using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
internal sealed class AmsiContextSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public AmsiContextSafeHandle()
        : base(ownsHandle: true)
    {
    }

    internal AmsiContextSafeHandle(nint contextHandle)
        : this()
    {
        SetHandle(contextHandle);
    }

    protected override bool ReleaseHandle()
    {
        Amsi.AmsiUninitialize(handle);
        return true;
    }
}
