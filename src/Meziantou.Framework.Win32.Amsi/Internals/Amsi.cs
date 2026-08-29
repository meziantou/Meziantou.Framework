using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Antimalware;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1416 // The containing APIs are Windows-only.
internal static partial class Amsi
{
    internal static bool AmsiResultIsMalware(AmsiResult result)
    {
        return result >= AmsiResult.AMSI_RESULT_DETECTED;
    }

    internal static int AmsiInitialize(string appName, out AmsiContextSafeHandle amsiContext)
    {
        var result = PInvoke.AmsiInitialize(appName, out var context);
        amsiContext = new AmsiContextSafeHandle((nint)context);
        return result;
    }

    internal static void AmsiUninitialize(IntPtr amsiContext)
    {
        PInvoke.AmsiUninitialize((HAMSICONTEXT)amsiContext);
    }

    internal static int AmsiOpenSession(AmsiContextSafeHandle amsiContext, out AmsiSessionSafeHandle session)
    {
        var contextAddRef = false;
        try
        {
            amsiContext.DangerousAddRef(ref contextAddRef);
            var result = PInvoke.AmsiOpenSession((HAMSICONTEXT)amsiContext.DangerousGetHandle(), out var nativeSession);
            session = new AmsiSessionSafeHandle(amsiContext, (nint)nativeSession);
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

    internal static int AmsiScanString(AmsiContextSafeHandle amsiContext, string payload, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
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

    internal static int AmsiScanBuffer(AmsiContextSafeHandle amsiContext, byte[] buffer, uint length, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
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
