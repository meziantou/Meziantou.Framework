using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

// An MsQuic handle (configuration, listener, connection, stream). A stream keeps a reference on its connection, so the
// connection is closed after its streams: MsQuic requires it.
//
// The context of the callback of a listener, a connection or a stream is a strong GCHandle of its managed object, freed
// once MsQuic reports the last event (STOP_COMPLETE, SHUTDOWN_COMPLETE) or the object is disposed. A weak handle would
// let the garbage collector collect an object whose only references are the continuations of its pending operations
// (the tasks MsQuic completes): its finalizer would then close the handle and abort the stream or the connection.
internal sealed class MsQuicSafeHandle : SafeHandle
{
    private readonly delegate* unmanaged[Cdecl]<nint, void> _close;
    private readonly MsQuicSafeHandle? _parent;

    public MsQuicSafeHandle(nint handle, delegate* unmanaged[Cdecl]<nint, void> close, MsQuicSafeHandle? parent = null)
        : base(invalidHandleValue: 0, ownsHandle: true)
    {
        _close = close;
        if (parent is not null)
        {
            var success = false;
            parent.DangerousAddRef(ref success);
            _parent = parent;
        }

        SetHandle(handle);
    }

    public override bool IsInvalid => handle == 0;


    protected override bool ReleaseHandle()
    {
        unsafe
        {
            _close(handle);
        }

        _parent?.DangerousRelease();
        return true;
    }
}
