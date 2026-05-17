using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

internal static partial class Amsi
{
    internal static bool AmsiResultIsMalware(AmsiResult result)
    {
        return result >= AmsiResult.AMSI_RESULT_DETECTED;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiInitialize", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial int AmsiInitialize(string appName, out AmsiContextSafeHandle amsiContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiUninitialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial void AmsiUninitialize(IntPtr amsiContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiOpenSession")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial int AmsiOpenSession(AmsiContextSafeHandle amsiContext, out AmsiSessionSafeHandle session);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiCloseSession")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial void AmsiCloseSession(AmsiContextSafeHandle amsiContext, IntPtr session);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiScanString", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial int AmsiScanString(AmsiContextSafeHandle amsiContext, string payload, string contentName, AmsiSessionSafeHandle session, out AmsiResult result);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Amsi.dll", EntryPoint = "AmsiScanBuffer", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial int AmsiScanBuffer(AmsiContextSafeHandle amsiContext, byte[] buffer, uint length, string contentName, AmsiSessionSafeHandle session, out AmsiResult result);
}
