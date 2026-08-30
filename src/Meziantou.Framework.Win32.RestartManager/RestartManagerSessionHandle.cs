using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Meziantou.Framework.Win32;

/// <summary>Owns a Restart Manager session handle and ends the session when released.</summary>
/// <remarks>
/// <para>
/// The Restart Manager documents no invalid session handle value, and 0 is a session like any other,
/// so -1 is used as the "no handle" sentinel instead of the usual 0.
/// </para>
/// <para>
/// The Restart Manager takes the session as a <see cref="uint"/> rather than as a handle, so its value has to be read
/// out of this <see cref="SafeHandle"/>. Reading it directly would leave nothing keeping this instance reachable for
/// the rest of the call, so the garbage collector could run the finalizer and end the session while a call is still
/// using it. Callers must go through <see cref="SafeHandleValue"/>, which holds a reference for the duration of the
/// call and also prevents a concurrent <see cref="SafeHandle.Dispose()"/> from ending the session mid-call.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class RestartManagerSessionHandle : SafeHandle
{
    public RestartManagerSessionHandle(uint sessionHandle)
        : base(invalidHandleValue: -1, ownsHandle: true)
    {
        SetHandle((nint)sessionHandle);
    }

    public override bool IsInvalid => handle == -1;

    protected override bool ReleaseHandle()
    {
        return PInvoke.RmEndSession((uint)handle) == WIN32_ERROR.ERROR_SUCCESS;
    }
}
