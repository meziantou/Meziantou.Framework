using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
internal sealed class AmsiSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal AmsiContextSafeHandle? Context { get; set; }

    public AmsiSessionSafeHandle()
        : base(ownsHandle: true)
    {
    }

    internal AmsiSessionSafeHandle(AmsiContextSafeHandle context, nint sessionHandle)
        : this()
    {
        Context = context;
        SetHandle(sessionHandle);
    }

    public override bool IsInvalid => Context is null || Context.IsInvalid || base.IsInvalid;

    protected override bool ReleaseHandle()
    {
        Debug.Assert(Context is not null);
        Amsi.AmsiCloseSession(Context, handle);
        return true;
    }
}
