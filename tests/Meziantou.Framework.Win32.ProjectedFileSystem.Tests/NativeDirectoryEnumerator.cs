using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Enumerates a directory through <c>NtQueryDirectoryFile</c> so tests can reach ProjFS behaviour that the
/// managed <see cref="Directory"/> APIs hide: those open a fresh handle per call, so they never trigger a
/// restart scan.
/// </summary>
internal sealed partial class NativeDirectoryEnumerator : IDisposable
{
    private const uint FileListDirectory = 0x0001;
    private const uint FileShareAll = 0x0007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileDirectoryInformation = 1;
    private const int BufferSize = 64 * 1024;

    // Offsets into FILE_DIRECTORY_INFORMATION
    private const int NextEntryOffsetOffset = 0;
    private const int FileNameLengthOffset = 60;
    private const int FileNameOffset = 64;

    private readonly SafeFileHandle _handle;

    private NativeDirectoryEnumerator(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static NativeDirectoryEnumerator Open(string path)
    {
        var handle = CreateFileW(path, FileListDirectory, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new NativeDirectoryEnumerator(handle);
    }

    /// <summary>Restarts the enumeration and reads it to completion, returning every name the driver reports.</summary>
    public List<string> FullScan()
    {
        var names = new List<string>();
        var restart = true;
        while (Query(restart, names))
        {
            restart = false;
        }

        return names;
    }

    /// <summary>Reads one batch of entries. Returns <see langword="false"/> once the enumeration is exhausted.</summary>
    private unsafe bool Query(bool restartScan, List<string> names)
    {
        var buffer = Marshal.AllocHGlobal(BufferSize);
        try
        {
            var status = NtQueryDirectoryFile(_handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, buffer, BufferSize,
                FileDirectoryInformation, returnSingleEntry: false, IntPtr.Zero, restartScan);

            // STATUS_NO_MORE_FILES and any other non-success status end the enumeration
            if (status != 0)
                return false;

            var entry = (byte*)buffer;
            while (true)
            {
                var nameLength = *(uint*)(entry + FileNameLengthOffset);
                names.Add(new string((char*)(entry + FileNameOffset), 0, (int)(nameLength / sizeof(char))));

                var nextEntryOffset = *(uint*)(entry + NextEntryOffsetOffset);
                if (nextEntryOffset is 0)
                    break;

                entry += nextEntryOffset;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryDirectoryFile(SafeFileHandle fileHandle, IntPtr @event, IntPtr apcRoutine, IntPtr apcContext,
        out IO_STATUS_BLOCK ioStatusBlock, IntPtr fileInformation, uint length, int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry, IntPtr fileName, [MarshalAs(UnmanagedType.U1)] bool restartScan);
}
