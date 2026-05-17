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
        var result = PInvoke.AmsiOpenSession((HAMSICONTEXT)amsiContext.DangerousGetHandle(), out var nativeSession);
        session = new AmsiSessionSafeHandle(amsiContext, (nint)nativeSession);
        return result;
    }

    internal static void AmsiCloseSession(AmsiContextSafeHandle amsiContext, IntPtr session)
    {
        PInvoke.AmsiCloseSession((HAMSICONTEXT)amsiContext.DangerousGetHandle(), (HAMSISESSION)session);
    }

    internal static int AmsiScanString(AmsiContextSafeHandle amsiContext, string payload, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
    {
        var returnValue = PInvoke.AmsiScanString((HAMSICONTEXT)amsiContext.DangerousGetHandle(), payload, contentName, (HAMSISESSION)session.DangerousGetHandle(), out var nativeResult);
        result = (AmsiResult)nativeResult;
        return returnValue;
    }

    internal static int AmsiScanBuffer(AmsiContextSafeHandle amsiContext, byte[] buffer, uint length, string contentName, AmsiSessionSafeHandle session, out AmsiResult result)
    {
        var returnValue = PInvoke.AmsiScanBuffer((HAMSICONTEXT)amsiContext.DangerousGetHandle(), buffer.AsSpan(0, checked((int)length)), contentName, (HAMSISESSION)session.DangerousGetHandle(), out var nativeResult);
        result = (AmsiResult)nativeResult;
        return returnValue;
    }
}
#pragma warning restore CA1416
