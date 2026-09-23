using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Diagnostics;

public static partial class MemoryDump
{
    private const int HResultFalse = 1; // S_FALSE
    private const int IsProcessSnapshotCallback = 16;

    // Same flags as the documentation sample and procdump
    private const PssCaptureFlags SnapshotCaptureFlags =
        PssCaptureFlags.VaClone |
        PssCaptureFlags.Handles |
        PssCaptureFlags.HandleNameInformation |
        PssCaptureFlags.HandleBasicInformation |
        PssCaptureFlags.HandleTypeSpecificInformation |
        PssCaptureFlags.HandleTrace |
        PssCaptureFlags.Threads |
        PssCaptureFlags.ThreadContext |
        PssCaptureFlags.ThreadContextExtended |
        PssCaptureFlags.CreateBreakaway |
        PssCaptureFlags.CreateBreakawayOptional |
        PssCaptureFlags.CreateUseVmAllocations |
        PssCaptureFlags.CreateReleaseSection;

    // A process cannot suspend itself while MiniDumpWriteDump reads its memory. The memory keeps changing, and some dumps
    // (e.g. WithHeap) fail with ERROR_PARTIAL_COPY on every attempt. So the dump is written from a snapshot of the process,
    // as Windows Error Reporting does: https://learn.microsoft.com/previous-versions/windows/desktop/proc_snap/export-a-process-snapshot-to-a-file
    [SupportedOSPlatform("windows")]
    private static void WriteWindows(string filePath, MemoryDumpType dumpType)
    {
        using var process = Process.GetCurrentProcess();
        var error = WindowsInterop.PssCaptureSnapshot(process.SafeHandle, SnapshotCaptureFlags, GetThreadContextFlags(), out var snapshotHandle);
        if (error is not 0)
            throw new Win32Exception(error);

        try
        {
            var callbackInformation = new MiniDumpCallbackInformation { CallbackRoutine = GetMiniDumpCallbackPointer() };
            using (var fileHandle = File.OpenHandle(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                if (WindowsInterop.MiniDumpWriteDump(snapshotHandle, (uint)process.Id, fileHandle, GetMiniDumpType(dumpType), exceptionParam: 0, userStreamParam: 0, ref callbackInformation))
                    return;

                error = Marshal.GetLastPInvokeError();
            }

            TryDeleteFile(filePath);
            throw new Win32Exception(error);
        }
        finally
        {
            _ = WindowsInterop.PssFreeSnapshot(process.SafeHandle, snapshotHandle);
        }
    }

    // CONTEXT_ALL
    private static uint GetThreadContextFlags()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 0x0010001F,
            Architecture.X86 => 0x0001003F,
            Architecture.Arm64 => 0x0040001F,
            _ => throw new PlatformNotSupportedException($"Memory dumps are not supported on {RuntimeInformation.ProcessArchitecture}"),
        };
    }

    private static nint GetMiniDumpCallbackPointer()
    {
        nint result;
        unsafe
        {
            result = (nint)(delegate* unmanaged<nint, nint, nint, int>)&MiniDumpCallback;
        }

        return result;
    }

    // MiniDumpWriteDump must be told that the handle is a snapshot, not a process
    [UnmanagedCallersOnly]
    private static int MiniDumpCallback(nint callbackParam, nint callbackInput, nint callbackOutput)
    {
        unsafe
        {
            // MINIDUMP_CALLBACK_INPUT is packed on 4 bytes: ULONG ProcessId; HANDLE ProcessHandle; ULONG CallbackType; ...
            var callbackType = Marshal.ReadInt32(callbackInput, sizeof(uint) + IntPtr.Size);
            if (callbackType is IsProcessSnapshotCallback)
            {
                // MINIDUMP_CALLBACK_OUTPUT.Status
                Marshal.WriteInt32(callbackOutput, HResultFalse);
            }
        }

        return 1; // TRUE
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

    // PSS_CAPTURE_FLAGS
    [Flags]
    private enum PssCaptureFlags : uint
    {
        VaClone = 0x00000001,
        Handles = 0x00000004,
        HandleNameInformation = 0x00000008,
        HandleBasicInformation = 0x00000010,
        HandleTypeSpecificInformation = 0x00000020,
        HandleTrace = 0x00000040,
        Threads = 0x00000080,
        ThreadContext = 0x00000100,
        ThreadContextExtended = 0x00000200,
        CreateBreakawayOptional = 0x04000000,
        CreateBreakaway = 0x08000000,
        CreateUseVmAllocations = 0x20000000,
        CreateReleaseSection = 0x80000000,
    }

    // MINIDUMP_CALLBACK_INFORMATION
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MiniDumpCallbackInformation
    {
        public nint CallbackRoutine;
        public nint CallbackParam;
    }

    // CsWin32 cannot generate MiniDumpWriteDump for AnyCPU because MINIDUMP_EXCEPTION_INFORMATION is architecture-specific.
    // DllImport rather than LibraryImport: the .NET 10 LibraryImport generator emits code rejected by the updated memory safety rules.
    [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "See comment above")]
    private static class WindowsInterop
    {
        // The optional structure parameters are always null here, so they are declared as pointers
        [DllImport("dbghelp.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static safe extern bool MiniDumpWriteDump(nint hProcess, uint processId, SafeFileHandle hFile, MiniDumpType dumpType, nint exceptionParam, nint userStreamParam, ref MiniDumpCallbackInformation callbackParam);

        // Returns a Win32 error code instead of setting the last error
        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static safe extern int PssCaptureSnapshot(SafeProcessHandle processHandle, PssCaptureFlags captureFlags, uint threadContextFlags, out nint snapshotHandle);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static safe extern int PssFreeSnapshot(SafeProcessHandle processHandle, nint snapshotHandle);
    }
}
