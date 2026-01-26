using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Base class for creating a virtual file system using Windows Projected File System (ProjFS).</summary>
/// <example>
/// <code>
/// public class MyVirtualFileSystem : ProjectedFileSystemBase
/// {
///     public MyVirtualFileSystem(string rootFolder) : base(rootFolder) { }
///
///     protected override IEnumerable&lt;ProjectedFileSystemEntry&gt; GetEntries(string path)
///     {
///         if (string.IsNullOrEmpty(path))
///         {
///             yield return ProjectedFileSystemEntry.File("file.txt", length: 100);
///             yield return ProjectedFileSystemEntry.Directory("folder");
///         }
///     }
///
///     protected override Stream OpenRead(string path)
///     {
///         if (AreFileNamesEqual(path, "file.txt"))
///             return new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
///         return null;
///     }
/// }
///
/// var rootPath = @"C:\MyVirtualFS";
/// Directory.CreateDirectory(rootPath);
/// using var vfs = new MyVirtualFileSystem(rootPath);
/// vfs.Start(options: null);
/// </code>
/// </example>
// https://github.com/Microsoft/Windows-classic-samples/blob/master/Samples/ProjectedFileSystem/regfsProvider.cpp
[SupportedOSPlatform("windows")]
public abstract class ProjectedFileSystemBase : IDisposable
{
    // Remaining work
    // * Use VersionInfo
    // * Async loading, GetEntries should return ValueTask (https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjcompletecommand)
    // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjupdatefileifneeded

    private readonly Guid _virtualizationInstanceId;
    private ProjFSSafeHandle? _instanceHandle;
    private NativeMethods.PrjCallbacks _callbacks;

    private readonly ConcurrentDictionary<Guid, DirectoryEnumerationSession> _activeEnumerations = new();
    private long _context;

    /// <summary>Gets the root folder path where the virtual file system is mounted.</summary>
    public string RootFolder { get; }

    /// <summary>Gets or sets the buffer size used for reading file data. Default is 4096 bytes.</summary>
    protected int BufferSize { get; set; } = 4096; // 4kB

    /// <summary>Initializes a new instance of the <see cref="ProjectedFileSystemBase"/> class.</summary>
    /// <param name="rootFolder">The root folder path where the virtual file system will be mounted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rootFolder"/> is null.</exception>
    /// <exception cref="NotSupportedException">Thrown when running in a 32-bit process.</exception>
    protected ProjectedFileSystemBase(string rootFolder)
    {
        ArgumentNullException.ThrowIfNull(rootFolder);

        if (!Environment.Is64BitProcess)
            throw new NotSupportedException("Projected File System is only supported on 64-bit process");

        RootFolder = Path.GetFullPath(rootFolder);
        _virtualizationInstanceId = Guid.NewGuid();
    }

    /// <summary>Starts the virtual file system with the specified options.</summary>
    /// <param name="options">Configuration options for the virtual file system. Can be null to use default settings.</param>
    /// <exception cref="NotSupportedException">Thrown when the Projected File System Windows feature is not installed.</exception>
    public void Start(ProjectedFileSystemStartOptions? options)
    {
        if (_instanceHandle is not null)
            return;

        try
        {
            var hr = NativeMethods.PrjMarkDirectoryAsPlaceholder(RootFolder, targetPathName: null, IntPtr.Zero, in _virtualizationInstanceId);
            hr.EnsureSuccess();
        }
        catch (DllNotFoundException ex)
        {
            throw new NotSupportedException("ProjFS is not supported on this machine. Make sure the optional windows feature 'Projected File System' is installed.", ex);
        }

        // Set up the callback table for the projection provider.
        _callbacks = new NativeMethods.PrjCallbacks
        {
            StartDirectoryEnumerationCallback = StartDirectoryEnumerationCallback,
            EndDirectoryEnumerationCallback = EndDirectoryEnumerationCallback,
            GetDirectoryEnumerationCallback = GetDirectoryEnumerationCallback,
            GetPlaceholderInfoCallback = GetPlaceholderInfoCallback,
            GetFileDataCallback = GetFileDataCallback,
            CancelCommandCallback = CancelCommandCallback,
            NotificationCallback = NotificationCallback,
            QueryFileNameCallback = QueryFileNameCallback,
        };

        var opt = new NativeMethods.PRJ_STARTVIRTUALIZING_OPTIONS();
        var notificationMappingsPtr = IntPtr.Zero;
        try
        {
            if (options is not null)
            {
                opt.Flags = options.UseNegativePathCache ? NativeMethods.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_USE_NEGATIVE_PATH_CACHE : NativeMethods.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_NONE;

                var structureSize = Marshal.SizeOf<NativeMethods.PRJ_NOTIFICATION_MAPPING>();
                notificationMappingsPtr = Marshal.AllocHGlobal(structureSize * options.Notifications.Count);

                for (var i = 0; i < options.Notifications.Count; i++)
                {
                    var copy = new NativeMethods.PRJ_NOTIFICATION_MAPPING()
                    {
                        NotificationBitMask = options.Notifications[i].NotificationType,
                        NotificationRoot = options.Notifications[i].Path ?? "",
                    };

                    Marshal.StructureToPtr(copy, IntPtr.Add(notificationMappingsPtr, structureSize * i), fDeleteOld: false);
                }

                opt.NotificationMappings = notificationMappingsPtr;
                opt.NotificationMappingsCount = (uint)options.Notifications.Count;
            }

            var context = ++_context;
            var hr = NativeMethods.PrjStartVirtualizing(RootFolder, in _callbacks, new IntPtr(context), in opt, out _instanceHandle);
            hr.EnsureSuccess();
        }
        finally
        {
            if (notificationMappingsPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(notificationMappingsPtr);
            }
        }
    }

    /// <summary>Gets the on-disk state of a file or directory in a virtual file system.</summary>
    /// <param name="path">The full path to the file or directory.</param>
    /// <returns>The on-disk state of the specified file or directory.</returns>
    public static PRJ_FILE_STATE GetOnDiskFileState(string path)
    {
        var hr = NativeMethods.PrjGetOnDiskFileState(path, out var state);
        hr.EnsureSuccess();
        return state;
    }

    /// <summary>Clears the negative path cache, which stores queries for non-existent paths.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the virtual file system is not started.</exception>
    protected void ClearNegativePathCache()
    {
        if (_instanceHandle is null)
            throw new InvalidOperationException("The service is not started");

        var result = NativeMethods.PrjClearNegativePathCache(_instanceHandle, out _);
        result.EnsureSuccess();
    }

    /// <summary>Deletes a file from the virtual file system projection.</summary>
    /// <param name="relativePath">The relative path of the file to delete.</param>
    /// <param name="updateFlags">Flags controlling which file states are allowed to be deleted.</param>
    /// <param name="failureReason">When the method returns false, contains the reason for the failure.</param>
    /// <returns><see langword="true"/> if the file was deleted; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the virtual file system is not started.</exception>
    protected bool DeleteFile(string relativePath, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
    {
        if (_instanceHandle is null)
            throw new InvalidOperationException("The service is not started");

        var hr = NativeMethods.PrjDeleteFile(_instanceHandle, relativePath, updateFlags, out failureReason);
        if (hr == HResult.ERROR_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION)
            return false;

        hr.EnsureSuccess();
        return true;
    }

    /// <summary>Stops the virtual file system.</summary>
    public void Stop()
    {
        if (_instanceHandle is null)
            return;

        _callbacks = default;
        _instanceHandle.Dispose();
        _instanceHandle = null;
    }

    /// <summary>Determines whether a file name matches a pattern using Windows file name matching rules.</summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="pattern">The pattern to match against. Supports wildcards (* and ?).</param>
    /// <returns><see langword="true"/> if the file name matches the pattern; otherwise, <see langword="false"/>.</returns>
    protected static bool FileNameMatch(string fileName, string pattern)
    {
        return NativeMethods.PrjFileNameMatch(fileName, pattern);
    }

    /// <summary>Compares two file names for equality using case-insensitive Windows file name comparison rules.</summary>
    /// <param name="fileName1">The first file name to compare.</param>
    /// <param name="fileName2">The second file name to compare.</param>
    /// <returns><see langword="true"/> if the file names are equal; otherwise, <see langword="false"/>.</returns>
    protected static bool AreFileNamesEqual(string fileName1, string fileName2)
    {
        return CompareFileName(fileName1, fileName2) == 0;
    }

    /// <summary>Compares two file names using Windows file name comparison rules.</summary>
    /// <param name="fileName1">The first file name to compare.</param>
    /// <param name="fileName2">The second file name to compare.</param>
    /// <returns>A value less than zero if <paramref name="fileName1"/> is less than <paramref name="fileName2"/>, zero if they are equal, or a value greater than zero if <paramref name="fileName1"/> is greater than <paramref name="fileName2"/>.</returns>
    protected static int CompareFileName(string fileName1, string fileName2)
    {
        return FileNameComparer.Instance.Compare(fileName1, fileName2);
    }

    /// <summary>When overridden in a derived class, returns the list of files and directories for the specified path.</summary>
    /// <param name="path">The relative path of the directory to enumerate. Empty string represents the root directory.</param>
    /// <returns>An enumerable collection of <see cref="ProjectedFileSystemEntry"/> objects representing the files and directories.</returns>
    protected abstract IEnumerable<ProjectedFileSystemEntry> GetEntries(string path);

    /// <summary>Gets metadata for a specific file or directory at the specified path.</summary>
    /// <param name="path">The relative path of the file or directory.</param>
    /// <returns>The <see cref="ProjectedFileSystemEntry"/> for the specified path, or <see langword="null"/> if not found.</returns>
    protected virtual ProjectedFileSystemEntry? GetEntry(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        return GetEntries(directory ?? "").FirstOrDefault(entry => CompareFileName(entry.Name, fileName) == 0);
    }

    /// <summary>When overridden in a derived class, opens a stream to read the content of a file at the specified path.</summary>
    /// <param name="path">The relative path of the file to read.</param>
    /// <returns>A <see cref="Stream"/> to read the file content, or <see langword="null"/> if the file does not exist.</returns>
    protected abstract Stream OpenRead(string path);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Stop();
    }

    private HResult QueryFileNameCallback(in NativeMethods.PrjCallbackData callbackData)
    {
        var fileName = callbackData.FilePathName;
        var entry = GetEntry(fileName);
        if (entry is null)
            return HResult.E_FILENOTFOUND;

        return HResult.S_OK;
    }

    private static HResult NotificationCallback(in NativeMethods.PrjCallbackData callbackData, bool isDirectory, NativeMethods.PRJ_NOTIFICATION notification, string destinationFileName, IntPtr operationParameters)
    {
        Debug.WriteLine($"{notification} {callbackData.FilePathName} {callbackData.Flags}");
        return HResult.S_OK;
    }

    private static HResult CancelCommandCallback(in NativeMethods.PrjCallbackData callbackData)
    {
        Debug.WriteLine($"CancelCommandCallback {callbackData.FilePathName}");
        return HResult.S_OK;
    }

    private HResult StartDirectoryEnumerationCallback(in NativeMethods.PrjCallbackData callbackData, in Guid enumerationId)
    {
        var entries = GetEntries(callbackData.FilePathName);
        _activeEnumerations[enumerationId] = new DirectoryEnumerationSession(entries);
        return HResult.S_OK;
    }

    private HResult EndDirectoryEnumerationCallback(in NativeMethods.PrjCallbackData callbackData, in Guid enumerationId)
    {
        if (_activeEnumerations.TryRemove(enumerationId, out _))
            return HResult.S_OK;

        return HResult.E_INVALIDARG;
    }

    private HResult GetDirectoryEnumerationCallback(in NativeMethods.PrjCallbackData callbackData, in Guid enumerationId, string searchExpression, IntPtr dirEntryBufferHandle)
    {
        if (!_activeEnumerations.TryGetValue(enumerationId, out var session))
        {
            return HResult.E_INVALIDARG;
        }

        if ((callbackData.Flags & NativeMethods.PRJ_CALLBACK_DATA_FLAGS.PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN) == NativeMethods.PRJ_CALLBACK_DATA_FLAGS.PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN)
        {
            session.Reset();
        }

        ProjectedFileSystemEntry? entry;
        while ((entry = session.GetNextEntry()) is not null)
        {
            var info = new NativeMethods.PRJ_FILE_BASIC_INFO
            {
                FileSize = entry.Length,
                IsDirectory = entry.IsDirectory,
            };

            var result = NativeMethods.PrjFillDirEntryBuffer(entry.Name, in info, dirEntryBufferHandle);
            if (!result.IsSuccess)
            {
                session.Reenqueue();
                return HResult.S_OK;
            }
        }

        return HResult.S_OK;
    }

    private HResult GetPlaceholderInfoCallback(in NativeMethods.PrjCallbackData callbackData)
    {
        var entry = GetEntry(callbackData.FilePathName);
        if (entry is null)
            return HResult.E_FILENOTFOUND;

        var info = new NativeMethods.PRJ_PLACEHOLDER_INFO();
        info.FileBasicInfo.IsDirectory = entry.IsDirectory;
        info.FileBasicInfo.FileSize = entry.Length;

#pragma warning disable CA2000 // Dispose objects before losing scope (ownHandle: false)
        var hr = NativeMethods.PrjWritePlaceholderInfo(
                    new ProjFSSafeHandle(callbackData.NamespaceVirtualizationContext, ownHandle: false),
                    callbackData.FilePathName, // Use the full relative path from the callback, not just the entry name
                    in info,
                    (uint)Marshal.SizeOf(info));
#pragma warning restore CA2000

        hr.EnsureSuccess();
        return HResult.S_OK;
    }

    private HResult GetFileDataCallback(in NativeMethods.PrjCallbackData callbackData, ulong byteOffset, uint length)
    {
        using var stream = OpenRead(callbackData.FilePathName);
        if (stream is null)
            return HResult.E_FILENOTFOUND;

        // Seek to the requested offset
        if (stream.CanSeek && byteOffset > 0)
        {
            stream.Seek((long)byteOffset, SeekOrigin.Begin);
        }

        using var safeHandle = new ProjFSSafeHandle(callbackData.NamespaceVirtualizationContext, ownHandle: false);

        var maxBufferSize = (uint)BufferSize;
        uint writeLength;
        uint alignment = 1;

        if (length > maxBufferSize)
        {
            var hr = NativeMethods.PrjGetVirtualizationInstanceInfo(safeHandle, out var instanceInfo);
            if (!hr.IsSuccess)
                return hr;
            alignment = instanceInfo.WriteAlignment;
        }

        // Allocate a buffer that adheres to the needed memory alignment.
        var bufferSize = Math.Min(length, maxBufferSize);
        var writeBuffer = NativeMethods.PrjAllocateAlignedBuffer(safeHandle, bufferSize);
        if (writeBuffer == IntPtr.Zero)
            return HResult.E_OUTOFMEMORY;

        var data = new byte[bufferSize];
        var currentOffset = byteOffset;
        var remainingLength = length;

        try
        {
            while (remainingLength > 0)
            {
                // Calculate how much to write in this iteration
                writeLength = Math.Min(remainingLength, bufferSize);

                // For alignment, truncate to alignment boundary (except for last chunk)
                if (remainingLength > writeLength && alignment > 1)
        {
                    var alignedEnd = BlockAlignTruncate(currentOffset + writeLength, alignment);
                    if (alignedEnd > currentOffset)
                    {
                        writeLength = (uint)(alignedEnd - currentOffset);
                    }
                }

                var read = stream.Read(data, 0, (int)writeLength);
                if (read == 0)
                    break;

                Marshal.Copy(data, 0, writeBuffer, read);

            // Write the data to the file in the local file system.
            var hr = NativeMethods.PrjWriteFileData(safeHandle,
                                  callbackData.DataStreamId,
                                  writeBuffer,
                                      currentOffset,
                                      (uint)read);

            if (!hr.IsSuccess)
                return hr;

                currentOffset += (uint)read;
                remainingLength -= (uint)read;
            }

            return HResult.S_OK;
            }
        finally
        {
        NativeMethods.PrjFreeAlignedBuffer(writeBuffer);
        }
    }

    private static ulong BlockAlignTruncate(ulong p, uint v)
    {
        // BlockAlignTruncate(): Aligns P on the previous V boundary (V must be != 0).
        // #define BlockAlignTruncate(P,V) ((P) & (0-((UINT64)(V))))
        return p & (0 - ((ulong)v));
    }
}
