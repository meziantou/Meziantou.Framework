using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Antimalware;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1416 // The containing APIs are Windows-only.
internal static partial class Amsi
{
    internal static HRESULT AmsiInitialize(string appName, out AmsiContextSafeHandle amsiContext)
    {
        var result = PInvoke.AmsiInitialize(appName, out var context);

        // Only take ownership of the handle the call actually produced: on failure the out parameter
        // carries no handle the caller is allowed to pass back to AmsiUninitialize.
        amsiContext = result.Failed ? new AmsiContextSafeHandle() : new AmsiContextSafeHandle((nint)context);
        return result;
    }

    internal static void AmsiUninitialize(IntPtr amsiContext)
    {
        PInvoke.AmsiUninitialize((HAMSICONTEXT)amsiContext);
    }

    internal static HRESULT AmsiOpenSession(AmsiContextSafeHandle amsiContext, out AmsiSessionSafeHandle session)
    {
        var contextAddRef = false;
        try
        {
            amsiContext.DangerousAddRef(ref contextAddRef);
            var result = PInvoke.AmsiOpenSession((HAMSICONTEXT)amsiContext.DangerousGetHandle(), out var nativeSession);
            session = result.Failed ? new AmsiSessionSafeHandle() : new AmsiSessionSafeHandle(amsiContext, (nint)nativeSession);
            return result;
        }
        finally
        {
            if (contextAddRef)
                amsiContext.DangerousRelease();
        }
    }

    internal static void AmsiCloseSession(AmsiContextSafeHandle amsiContext, IntPtr session)
    {
        // Called from AmsiSessionSafeHandle.ReleaseHandle, which already holds a reference on the context.
        PInvoke.AmsiCloseSession((HAMSICONTEXT)amsiContext.DangerousGetHandle(), (HAMSISESSION)session);
    }

    internal static HRESULT AmsiScanString(AmsiContextSafeHandle amsiContext, string payload, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
    {
        var contextAddRef = false;
        var sessionAddRef = false;
        try
        {
            // DangerousGetHandle opts out of the lifetime guarantees SafeHandle exists to provide: the handles
            // are unreferenced in this frame once the raw pointers are read, so a collection during the scan
            // could run the critical finalizer and uninitialize the context while the provider is still using it.
            amsiContext.DangerousAddRef(ref contextAddRef);
            session.DangerousAddRef(ref sessionAddRef);

            var returnValue = PInvoke.AmsiScanString((HAMSICONTEXT)amsiContext.DangerousGetHandle(), payload, contentName, (HAMSISESSION)session.DangerousGetHandle(), out var nativeResult);
            result = (AmsiResult)nativeResult;
            return returnValue;
        }
        finally
        {
            if (sessionAddRef)
                session.DangerousRelease();

            if (contextAddRef)
                amsiContext.DangerousRelease();
        }
    }

    internal static HRESULT AmsiScanBuffer(AmsiContextSafeHandle amsiContext, byte[] buffer, uint length, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
    {
        var contextAddRef = false;
        var sessionAddRef = false;
        try
        {
            amsiContext.DangerousAddRef(ref contextAddRef);
            session.DangerousAddRef(ref sessionAddRef);

            var returnValue = PInvoke.AmsiScanBuffer((HAMSICONTEXT)amsiContext.DangerousGetHandle(), buffer.AsSpan(0, checked((int)length)), contentName, (HAMSISESSION)session.DangerousGetHandle(), out var nativeResult);
            result = (AmsiResult)nativeResult;
            return returnValue;
        }
        finally
        {
            if (sessionAddRef)
                session.DangerousRelease();

            if (contextAddRef)
                amsiContext.DangerousRelease();
        }
    }
}
#pragma warning restore CA1416
