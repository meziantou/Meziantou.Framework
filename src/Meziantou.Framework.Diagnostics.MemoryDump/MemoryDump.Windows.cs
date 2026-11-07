using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using Windows.Win32.System.Diagnostics.ProcessSnapshotting;

namespace Meziantou.Framework.Diagnostics;

public static partial class MemoryDump
{
    // Same flags as the documentation sample and procdump
    private const PSS_CAPTURE_FLAGS SnapshotCaptureFlags =
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_VA_CLONE |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLES |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_NAME_INFORMATION |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_BASIC_INFORMATION |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_TYPE_SPECIFIC_INFORMATION |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_TRACE |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREADS |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREAD_CONTEXT |
        PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREAD_CONTEXT_EXTENDED |
        PSS_CAPTURE_FLAGS.PSS_CREATE_BREAKAWAY |
        PSS_CAPTURE_FLAGS.PSS_CREATE_BREAKAWAY_OPTIONAL |
        PSS_CAPTURE_FLAGS.PSS_CREATE_USE_VM_ALLOCATIONS |
        PSS_CAPTURE_FLAGS.PSS_CREATE_RELEASE_SECTION;

    // A process cannot suspend itself while MiniDumpWriteDump reads its memory. The memory keeps changing, and some dumps
    // (e.g. WithHeap) fail with ERROR_PARTIAL_COPY on every attempt. So the dump is written from a snapshot of the process,
    // as Windows Error Reporting does: https://learn.microsoft.com/previous-versions/windows/desktop/proc_snap/export-a-process-snapshot-to-a-file
    [SupportedOSPlatform("windows8.1")]
    private static unsafe void WriteWindows(string filePath, MemoryDumpType dumpType)
    {
        using var process = Process.GetCurrentProcess();
        var error = PInvoke.PssCaptureSnapshot(process.SafeHandle, SnapshotCaptureFlags, (uint)GetThreadContextFlags(), out var snapshotHandle);
        if (error is not 0)
            throw new Win32Exception((int)error);

        try
        {
            var callbackInformation = new MiniDumpCallbackInformation { CallbackRoutine = &MiniDumpCallback };
            int lastError;
            using (var fileHandle = File.OpenHandle(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                if (WindowsInterop.MiniDumpWriteDump((nint)snapshotHandle.Value, (uint)process.Id, fileHandle, (uint)GetMiniDumpType(dumpType), exceptionParam: null, userStreamParam: null, &callbackInformation))
                    return;

                lastError = Marshal.GetLastPInvokeError();
            }

            TryDeleteFile(filePath);
            throw new Win32Exception(lastError);
        }
        finally
        {
            _ = PInvoke.PssFreeSnapshot(process.SafeHandle, snapshotHandle);
        }
    }

    private static CONTEXT_FLAGS GetThreadContextFlags()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => CONTEXT_FLAGS.CONTEXT_ALL_AMD64,
            Architecture.X86 => CONTEXT_FLAGS.CONTEXT_ALL_X86,
            Architecture.Arm64 => CONTEXT_FLAGS.CONTEXT_ALL_ARM64,
            _ => throw new PlatformNotSupportedException($"Memory dumps are not supported on {RuntimeInformation.ProcessArchitecture}"),
        };
    }

    // MiniDumpWriteDump must be told that the handle is a snapshot, not a process
    [UnmanagedCallersOnly]
    private static unsafe BOOL MiniDumpCallback(void* callbackParam, byte* callbackInput, MINIDUMP_CALLBACK_OUTPUT* callbackOutput)
    {
        // MINIDUMP_CALLBACK_INPUT is packed on 4 bytes: ULONG ProcessId; HANDLE ProcessHandle; ULONG CallbackType; ...
        var callbackType = (MINIDUMP_CALLBACK_TYPE)Unsafe.ReadUnaligned<int>(callbackInput + sizeof(uint) + sizeof(nint));
        if (callbackType is MINIDUMP_CALLBACK_TYPE.IsProcessSnapshotCallback)
        {
            callbackOutput->Status = HRESULT.S_FALSE;
        }

        return true;
    }

    // Same flags as dotnet-dump
    private static MINIDUMP_TYPE GetMiniDumpType(MemoryDumpType dumpType)
    {
        return dumpType switch
        {
            MemoryDumpType.Normal =>
                MINIDUMP_TYPE.MiniDumpNormal |
                MINIDUMP_TYPE.MiniDumpWithDataSegs |
                MINIDUMP_TYPE.MiniDumpWithHandleData |
                MINIDUMP_TYPE.MiniDumpWithThreadInfo,

            MemoryDumpType.WithHeap =>
                MINIDUMP_TYPE.MiniDumpWithDataSegs |
                MINIDUMP_TYPE.MiniDumpWithHandleData |
                MINIDUMP_TYPE.MiniDumpWithUnloadedModules |
                MINIDUMP_TYPE.MiniDumpWithFullMemoryInfo |
                MINIDUMP_TYPE.MiniDumpWithThreadInfo |
                MINIDUMP_TYPE.MiniDumpWithTokenInformation |
                MINIDUMP_TYPE.MiniDumpWithPrivateReadWriteMemory,

            MemoryDumpType.Triage =>
                MINIDUMP_TYPE.MiniDumpFilterTriage |
                MINIDUMP_TYPE.MiniDumpIgnoreInaccessibleMemory |
                MINIDUMP_TYPE.MiniDumpWithoutOptionalData |
                MINIDUMP_TYPE.MiniDumpWithProcessThreadData |
                MINIDUMP_TYPE.MiniDumpFilterModulePaths |
                MINIDUMP_TYPE.MiniDumpWithUnloadedModules |
                MINIDUMP_TYPE.MiniDumpFilterMemory |
                MINIDUMP_TYPE.MiniDumpWithHandleData,

            MemoryDumpType.Full =>
                MINIDUMP_TYPE.MiniDumpWithFullMemory |
                MINIDUMP_TYPE.MiniDumpWithDataSegs |
                MINIDUMP_TYPE.MiniDumpWithHandleData |
                MINIDUMP_TYPE.MiniDumpWithUnloadedModules |
                MINIDUMP_TYPE.MiniDumpWithFullMemoryInfo |
                MINIDUMP_TYPE.MiniDumpWithThreadInfo |
                MINIDUMP_TYPE.MiniDumpWithTokenInformation,

            _ => throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, message: null),
        };
    }

    // MINIDUMP_CALLBACK_INFORMATION. CsWin32 cannot generate it for AnyCPU because the callback input contains a CONTEXT.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private unsafe struct MiniDumpCallbackInformation
    {
        public delegate* unmanaged<void*, byte*, MINIDUMP_CALLBACK_OUTPUT*, BOOL> CallbackRoutine;
        public void* CallbackParam;
    }

    private static partial class WindowsInterop
    {
        // CsWin32 cannot generate MiniDumpWriteDump for AnyCPU because MINIDUMP_EXCEPTION_INFORMATION is architecture-specific.
        // The optional structure parameters are always null here, so they are declared as void*. The LibraryImport generator
        // cannot see the types generated by CsWin32, so the snapshot handle and the dump type are declared as primitives.
        [LibraryImport("dbghelp.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool MiniDumpWriteDump(nint hProcess, uint processId, SafeFileHandle hFile, uint dumpType, void* exceptionParam, void* userStreamParam, MiniDumpCallbackInformation* callbackParam);
    }
}
