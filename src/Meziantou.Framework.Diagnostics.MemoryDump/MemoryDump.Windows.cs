using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Diagnostics;

public static partial class MemoryDump
{
    private const int MaxMiniDumpAttempts = 5;
    private const int ERROR_PARTIAL_COPY = 0x12B;
    private const int HRESULT_ERROR_PARTIAL_COPY = unchecked((int)0x8007012B);

    [SupportedOSPlatform("windows")]
    private static void WriteWindows(string filePath, MemoryDumpType dumpType)
    {
        var miniDumpType = GetMiniDumpType(dumpType);
        using var process = Process.GetCurrentProcess();
        var error = 0;
        for (var attempt = 0; attempt < MaxMiniDumpAttempts; attempt++)
        {
            using (var fileHandle = File.OpenHandle(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                if (WindowsInterop.MiniDumpWriteDump(process.SafeHandle, (uint)process.Id, fileHandle, miniDumpType, exceptionParam: 0, userStreamParam: 0, callbackParam: 0))
                    return;

                error = Marshal.GetLastPInvokeError();
            }

            // The memory of a running process can change while it is read, so MiniDumpWriteDump sometimes fails with
            // ERROR_PARTIAL_COPY. dotnet-dump retries in this case too.
            if (error is not (ERROR_PARTIAL_COPY or HRESULT_ERROR_PARTIAL_COPY))
                break;
        }

        TryDeleteFile(filePath);
        throw new Win32Exception(error);
    }

    // Same flags as dotnet-dump
    private static MiniDumpType GetMiniDumpType(MemoryDumpType dumpType)
    {
        return dumpType switch
        {
            MemoryDumpType.Normal =>
                MiniDumpType.Normal |
                MiniDumpType.WithDataSegs |
                MiniDumpType.WithHandleData |
                MiniDumpType.WithThreadInfo,

            MemoryDumpType.WithHeap =>
                MiniDumpType.WithDataSegs |
                MiniDumpType.WithHandleData |
                MiniDumpType.WithUnloadedModules |
                MiniDumpType.WithFullMemoryInfo |
                MiniDumpType.WithThreadInfo |
                MiniDumpType.WithTokenInformation |
                MiniDumpType.WithPrivateReadWriteMemory,

            MemoryDumpType.Triage =>
                MiniDumpType.FilterTriage |
                MiniDumpType.IgnoreInaccessibleMemory |
                MiniDumpType.WithoutOptionalData |
                MiniDumpType.WithProcessThreadData |
                MiniDumpType.FilterModulePaths |
                MiniDumpType.WithUnloadedModules |
                MiniDumpType.FilterMemory |
                MiniDumpType.WithHandleData,

            MemoryDumpType.Full =>
                MiniDumpType.WithFullMemory |
                MiniDumpType.WithDataSegs |
                MiniDumpType.WithHandleData |
                MiniDumpType.WithUnloadedModules |
                MiniDumpType.WithFullMemoryInfo |
                MiniDumpType.WithThreadInfo |
                MiniDumpType.WithTokenInformation,

            _ => throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, message: null),
        };
    }

    // MINIDUMP_TYPE
    [Flags]
    private enum MiniDumpType : uint
    {
        Normal = 0x00000000,
        WithDataSegs = 0x00000001,
        WithFullMemory = 0x00000002,
        WithHandleData = 0x00000004,
        FilterMemory = 0x00000008,
        WithUnloadedModules = 0x00000020,
        FilterModulePaths = 0x00000080,
        WithProcessThreadData = 0x00000100,
        WithPrivateReadWriteMemory = 0x00000200,
        WithoutOptionalData = 0x00000400,
        WithFullMemoryInfo = 0x00000800,
        WithThreadInfo = 0x00001000,
        IgnoreInaccessibleMemory = 0x00020000,
        WithTokenInformation = 0x00040000,
        FilterTriage = 0x00100000,
    }

    private static class WindowsInterop
    {
        // CsWin32 cannot generate this API for AnyCPU because MINIDUMP_EXCEPTION_INFORMATION is architecture-specific.
        // The optional structure parameters are always null here, so they are declared as pointers.
        // DllImport rather than LibraryImport: the .NET 10 LibraryImport generator emits code rejected by the updated memory safety rules
        [DllImport("dbghelp.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "See comment above")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static safe extern bool MiniDumpWriteDump(SafeProcessHandle hProcess, uint processId, SafeFileHandle hFile, MiniDumpType dumpType, nint exceptionParam, nint userStreamParam, nint callbackParam);
    }
}
