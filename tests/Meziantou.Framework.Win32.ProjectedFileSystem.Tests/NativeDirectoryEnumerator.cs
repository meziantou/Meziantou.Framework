using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Wdk.Storage.FileSystem;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Enumerates a directory through <c>NtQueryDirectoryFile</c> so tests can reach ProjFS behaviour that the
/// managed <see cref="Directory"/> APIs hide: those open a fresh handle per call, so they never trigger a
/// restart scan.
/// </summary>
internal sealed class NativeDirectoryEnumerator : IDisposable
{
    private const int BufferSize = 64 * 1024;

    private readonly SafeFileHandle _handle;

    private NativeDirectoryEnumerator(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static NativeDirectoryEnumerator Open(string path)
    {
        var handle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_LIST_DIRECTORY,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            lpSecurityAttributes: null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            hTemplateFile: null);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new NativeDirectoryEnumerator(handle);
    }

    /// <summary>Restarts the enumeration and reads it to completion, returning every name the driver reports.</summary>
    /// <param name="searchExpression">The pattern to pass to the driver, or <see langword="null"/> to enumerate everything.</param>
    public List<string> FullScan(string? searchExpression = null)
    {
        var names = new List<string>();
        var restart = true;
        while (Query(restart, searchExpression, names))
        {
            restart = false;
        }

        return names;
    }

    /// <summary>Reads one batch of entries. Returns <see langword="false"/> once the enumeration is exhausted.</summary>
    private unsafe bool Query(bool restartScan, string? searchExpression, List<string> names)
    {
        var buffer = new byte[BufferSize];
        var handleAcquired = false;
        try
        {
            _handle.DangerousAddRef(ref handleAcquired);

            fixed (char* searchExpressionPtr = searchExpression)
            {
                var searchExpressionLength = checked((ushort)((searchExpression?.Length ?? 0) * sizeof(char)));
                UNICODE_STRING? fileName = searchExpression is null ? null : new UNICODE_STRING
                {
                    Length = searchExpressionLength,
                    MaximumLength = searchExpressionLength,
                    Buffer = searchExpressionPtr,
                };

                var status = Windows.Wdk.PInvoke.NtQueryDirectoryFile(
                    (HANDLE)_handle.DangerousGetHandle(),
                    Event: default,
                    ApcRoutine: null,
                    ApcContext: null,
                    IoStatusBlock: out _,
                    buffer,
                    FILE_INFORMATION_CLASS.FileDirectoryInformation,
                    ReturnSingleEntry: false,
                    fileName,
                    restartScan);

                // STATUS_NO_MORE_FILES and any other non-success status end the enumeration
                if (status.Value is not 0)
                    return false;
            }

            fixed (byte* bufferPtr = buffer)
            {
                var entry = (FILE_DIRECTORY_INFORMATION*)bufferPtr;
                while (true)
                {
                    names.Add(new string(entry->FileName.AsSpan((int)(entry->FileNameLength / sizeof(char)))));

                    if (entry->NextEntryOffset is 0)
                        break;

                    entry = (FILE_DIRECTORY_INFORMATION*)((byte*)entry + entry->NextEntryOffset);
                }
            }

            return true;
        }
        finally
        {
            if (handleAcquired)
            {
                _handle.DangerousRelease();
            }
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
