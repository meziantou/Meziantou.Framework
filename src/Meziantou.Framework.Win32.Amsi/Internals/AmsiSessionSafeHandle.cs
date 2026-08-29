using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows10.0.10240")]
internal sealed class AmsiSessionSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly AmsiContextSafeHandle? _context;

    public AmsiSessionSafeHandle()
        : base(ownsHandle: true)
    {
    }

    internal AmsiSessionSafeHandle(AmsiContextSafeHandle context, nint sessionHandle)
        : this()
    {
        // AmsiOpenSession failed, so there is no session to close and no reason to keep the context alive.
        if (sessionHandle is 0)
            return;

        // ReleaseHandle passes the context back to AmsiCloseSession, so the context must stay initialized
        // until this session is closed. Without this reference, disposing the context first releases the
        // native handle while this session still points at it. SafeHandle.IsInvalid cannot detect that:
        // it reads the raw handle value, which Dispose never clears.
        var success = false;
        context.DangerousAddRef(ref success);
        _context = context;
        SetHandle(sessionHandle);
    }

    protected override bool ReleaseHandle()
    {
        Debug.Assert(_context is not null);

        // Safe without an extra AddRef: the reference taken in the constructor is still held here.
        Amsi.AmsiCloseSession(_context, handle);
        _context.DangerousRelease();
        return true;
    }
}
