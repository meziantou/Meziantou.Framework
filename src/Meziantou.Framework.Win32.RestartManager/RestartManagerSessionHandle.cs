using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Meziantou.Framework.Win32;

/// <summary>Owns a Restart Manager session handle and ends the session when released.</summary>
/// <remarks>
/// The Restart Manager documents no invalid session handle value, and 0 is a session like any other,
/// so -1 is used as the "no handle" sentinel instead of the usual 0.
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

    public uint SessionHandle => (uint)handle;

    protected override bool ReleaseHandle()
    {
        return PInvoke.RmEndSession((uint)handle) == WIN32_ERROR.ERROR_SUCCESS;
    }
}
