using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Meziantou.Framework.Win32.Natives;

internal static class FileHandleHelper
{
    /// <summary>
    ///     Opens a file or a directory so its metadata can be read. <c>FILE_FLAG_BACKUP_SEMANTICS</c> is what makes a directory
    ///     openable, which is why <see cref="File.OpenHandle(string, FileMode, FileAccess, FileShare, FileOptions, long)"/> cannot
    ///     be used here: it never sets that flag, so it fails on a directory with an access denied error.
    ///     <c>FILE_READ_ATTRIBUTES</c> is all the metadata operations need, and asking for no more than that keeps the call
    ///     working on files the caller is not allowed to read.
    /// </summary>
    /// <exception cref="Win32Exception">Thrown when the file or directory cannot be opened.</exception>
    [SupportedOSPlatform("windows5.1.2600")]
    internal static SafeFileHandle OpenFileOrDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var handle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            lpSecurityAttributes: null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            hTemplateFile: null);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return handle;
    }
}
