using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

[SupportedOSPlatform("windows10.0.17763")]
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
        var result = NativeMethods.PrjStopVirtualizing(handle);
        return result.IsSuccess;
    }
}
