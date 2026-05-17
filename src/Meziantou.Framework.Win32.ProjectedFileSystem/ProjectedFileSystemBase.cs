using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using ProjFs = Windows.Win32.Storage.ProjectedFileSystem;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Base class for creating a virtual file system using Windows Projected File System (ProjFS).</summary>
/// <example>
/// <code>
/// public class MyVirtualFileSystem : ProjectedFileSystemBase
/// {
///     public MyVirtualFileSystem(string rootFolder) : base(rootFolder) { }
///
///     protected override ValueTask&lt;IEnumerable&lt;ProjectedFileSystemEntry&gt;&gt; GetEntriesAsync(string path)
///     {
///         if (string.IsNullOrEmpty(path))
///         {
///             return ValueTask.FromResult&lt;IEnumerable&lt;ProjectedFileSystemEntry&gt;&gt;(
///             [
///                 ProjectedFileSystemEntry.File("file.txt", length: 100),
///                 ProjectedFileSystemEntry.Directory("folder"),
///             ]);
///         }
///
///         return ValueTask.FromResult(Enumerable.Empty&lt;ProjectedFileSystemEntry&gt;());
///     }
///
///     protected override ValueTask&lt;Stream?&gt; OpenReadAsync(string path)
///     {
///         if (AreFileNamesEqual(path, "file.txt"))
///             return ValueTask.FromResult&lt;Stream?&gt;(new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!")));
///
///         return ValueTask.FromResult&lt;Stream?&gt;(null);
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
[SupportedOSPlatform("windows10.0.17763")]
public abstract class ProjectedFileSystemBase : IDisposable
{
    // Remaining work
    // * Use VersionInfo
    // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjupdatefileifneeded

    private readonly Guid _virtualizationInstanceId;
    private ProjFSSafeHandle? _instanceHandle;
    private ProjFs.PRJ_CALLBACKS _callbacks;
    private GCHandle _instanceContextHandle;

    private readonly ConcurrentDictionary<Guid, DirectoryEnumerationSession> _activeEnumerations = new();

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
    public unsafe void Start(ProjectedFileSystemStartOptions? options)
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
        _callbacks = new ProjFs.PRJ_CALLBACKS
        {
            StartDirectoryEnumerationCallback = StartDirectoryEnumerationCallbackNative,
            EndDirectoryEnumerationCallback = EndDirectoryEnumerationCallbackNative,
            GetDirectoryEnumerationCallback = GetDirectoryEnumerationCallbackNative,
            GetPlaceholderInfoCallback = GetPlaceholderInfoCallbackNative,
            GetFileDataCallback = GetFileDataCallbackNative,
            CancelCommandCallback = CancelCommandCallbackNative,
            NotificationCallback = NotificationCallbackNative,
            QueryFileNameCallback = QueryFileNameCallbackNative,
        };

        var opt = new ProjFs.PRJ_STARTVIRTUALIZING_OPTIONS();
        ProjFs.PRJ_NOTIFICATION_MAPPING* notificationMappingsPointer = null;
        IntPtr[]? notificationRoots = null;
        var instanceContextHandle = GCHandle.Alloc(this);
        try
        {
            if (options is not null)
            {
                opt.Flags = options.UseNegativePathCache ? ProjFs.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_USE_NEGATIVE_PATH_CACHE : ProjFs.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_NONE;

                var structureSize = sizeof(ProjFs.PRJ_NOTIFICATION_MAPPING);
                notificationRoots = new IntPtr[options.Notifications.Count];
                notificationMappingsPointer = (ProjFs.PRJ_NOTIFICATION_MAPPING*)Marshal.AllocHGlobal(structureSize * options.Notifications.Count);

                for (var i = 0; i < options.Notifications.Count; i++)
                {
                    var notificationRoot = options.Notifications[i].Path ?? "";
                    notificationRoots[i] = Marshal.StringToHGlobalUni(notificationRoot);
                    notificationMappingsPointer[i] = new ProjFs.PRJ_NOTIFICATION_MAPPING
                    {
                        NotificationBitMask = (ProjFs.PRJ_NOTIFY_TYPES)options.Notifications[i].NotificationType,
                        NotificationRoot = (char*)notificationRoots[i],
                    };
                }

                opt.NotificationMappings = notificationMappingsPointer;
                opt.NotificationMappingsCount = (uint)options.Notifications.Count;
            }

            var context = GCHandle.ToIntPtr(instanceContextHandle);
            var hr = NativeMethods.PrjStartVirtualizing(RootFolder, in _callbacks, context, in opt, out _instanceHandle);
            hr.EnsureSuccess();
            _instanceContextHandle = instanceContextHandle;
            instanceContextHandle = default;
        }
        finally
        {
            if (instanceContextHandle.IsAllocated)
            {
                instanceContextHandle.Free();
            }

            if (notificationRoots is not null)
            {
                foreach (var notificationRoot in notificationRoots)
                {
                    if (notificationRoot != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(notificationRoot);
                    }
                }
            }

            if (notificationMappingsPointer is not null)
            {
                Marshal.FreeHGlobal((IntPtr)notificationMappingsPointer);
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

        if (_instanceContextHandle.IsAllocated)
        {
            _instanceContextHandle.Free();
            _instanceContextHandle = default;
        }
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
    /// <returns>A <see cref="ValueTask{TResult}"/> resolving to an enumerable collection of <see cref="ProjectedFileSystemEntry"/> objects representing the files and directories.</returns>
    protected abstract ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path);

    /// <summary>Gets metadata for a specific file or directory at the specified path.</summary>
    /// <param name="path">The relative path of the file or directory.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> resolving to the <see cref="ProjectedFileSystemEntry"/> for the specified path, or <see langword="null"/> if not found.</returns>
    protected virtual async ValueTask<ProjectedFileSystemEntry?> GetEntryAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        var entries = await GetEntriesAsync(directory ?? "").ConfigureAwait(false);
        return entries.FirstOrDefault(entry => CompareFileName(entry.Name, fileName) == 0);
    }

    /// <summary>When overridden in a derived class, opens a stream to read the content of a file at the specified path.</summary>
    /// <param name="path">The relative path of the file to read.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> resolving to a <see cref="Stream"/> to read the file content, or <see langword="null"/> if the file does not exist.</returns>
    protected abstract ValueTask<Stream?> OpenReadAsync(string path);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Stop();
    }

    private static unsafe HRESULT QueryFileNameCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData)
    {
        if (callbackData is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.QueryFileNameCallback(in *callbackData));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT StartDirectoryEnumerationCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData, Guid* enumerationId)
    {
        if (callbackData is null || enumerationId is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.StartDirectoryEnumerationCallback(in *callbackData, in *enumerationId));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT EndDirectoryEnumerationCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData, Guid* enumerationId)
    {
        if (callbackData is null || enumerationId is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.EndDirectoryEnumerationCallback(in *callbackData, in *enumerationId));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT GetDirectoryEnumerationCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData, Guid* enumerationId, PCWSTR searchExpression, ProjFs.PRJ_DIR_ENTRY_BUFFER_HANDLE dirEntryBufferHandle)
    {
        if (callbackData is null || enumerationId is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.GetDirectoryEnumerationCallback(in *callbackData, in *enumerationId, searchExpression.ToString() ?? "", (IntPtr)dirEntryBufferHandle));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT GetPlaceholderInfoCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData)
    {
        if (callbackData is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.GetPlaceholderInfoCallback(in *callbackData));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT GetFileDataCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData, ulong byteOffset, uint length)
    {
        if (callbackData is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            var instance = GetInstance(callbackData);
            return ToHRESULT(instance.GetFileDataCallback(in *callbackData, byteOffset, length));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe HRESULT NotificationCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData, BOOLEAN isDirectory, ProjFs.PRJ_NOTIFICATION notification, PCWSTR destinationFileName, ProjFs.PRJ_NOTIFICATION_PARAMETERS* operationParameters)
    {
        if (callbackData is null)
            return ToHRESULT(HResult.E_INVALIDARG);

        try
        {
            return ToHRESULT(NotificationCallback(in *callbackData, isDirectory, notification, destinationFileName.ToString() ?? "", (IntPtr)operationParameters));
        }
        catch (Exception ex)
        {
            return (HRESULT)Marshal.GetHRForException(ex);
        }
    }

    private static unsafe void CancelCommandCallbackNative(ProjFs.PRJ_CALLBACK_DATA* callbackData)
    {
        if (callbackData is null)
            return;

        try
        {
            _ = CancelCommandCallback(in *callbackData);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private static unsafe ProjectedFileSystemBase GetInstance(ProjFs.PRJ_CALLBACK_DATA* callbackData)
    {
        if (callbackData->InstanceContext is null)
            throw new InvalidOperationException("Cannot resolve callback instance context");

        var handle = GCHandle.FromIntPtr((IntPtr)callbackData->InstanceContext);
        return (ProjectedFileSystemBase)handle.Target!;
    }

    private static HRESULT ToHRESULT(HResult value)
    {
        return (HRESULT)value.Value;
    }

    private HResult QueryFileNameCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData)
    {
        return ExecuteCallback(
            in callbackData,
            QueryFileNameCallbackAsync(callbackData.FilePathName.ToString() ?? ""),
            extendedParameters: null);
    }

    private async ValueTask<HResult> QueryFileNameCallbackAsync(string fileName)
    {
        var entry = await GetEntryAsync(fileName).ConfigureAwait(false);
        return entry is null ? HResult.E_FILENOTFOUND : HResult.S_OK;
    }

    private static HResult NotificationCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, bool isDirectory, ProjFs.PRJ_NOTIFICATION notification, string destinationFileName, IntPtr operationParameters)
    {
        Debug.WriteLine($"{notification} {callbackData.FilePathName} {callbackData.Flags}");
        return HResult.S_OK;
    }

    private static HResult CancelCommandCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData)
    {
        Debug.WriteLine($"CancelCommandCallback {callbackData.FilePathName}");
        return HResult.S_OK;
    }

    private HResult StartDirectoryEnumerationCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
    {
        return ExecuteCallback(
            in callbackData,
            StartDirectoryEnumerationCallbackAsync(callbackData.FilePathName.ToString() ?? "", enumerationId),
            extendedParameters: null);
    }

    private async ValueTask<HResult> StartDirectoryEnumerationCallbackAsync(string filePathName, Guid enumerationId)
    {
        var entries = await GetEntriesAsync(filePathName).ConfigureAwait(false);
        _activeEnumerations[enumerationId] = new DirectoryEnumerationSession(entries);
        return HResult.S_OK;
    }

    private HResult EndDirectoryEnumerationCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId)
    {
        if (_activeEnumerations.TryRemove(enumerationId, out _))
            return HResult.S_OK;

        return HResult.E_INVALIDARG;
    }

    private HResult GetDirectoryEnumerationCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, in Guid enumerationId, string searchExpression, IntPtr dirEntryBufferHandle)
    {
        if (!_activeEnumerations.TryGetValue(enumerationId, out var session))
        {
            return HResult.E_INVALIDARG;
        }

        if ((callbackData.Flags & ProjFs.PRJ_CALLBACK_DATA_FLAGS.PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN) == ProjFs.PRJ_CALLBACK_DATA_FLAGS.PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN)
        {
            session.Reset();
        }

        ProjectedFileSystemEntry? entry;
        while ((entry = session.GetNextEntry()) is not null)
        {
            var info = new ProjFs.PRJ_FILE_BASIC_INFO
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

    private HResult GetPlaceholderInfoCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData)
    {
        return ExecuteCallback(
            in callbackData,
            GetPlaceholderInfoCallbackAsync(callbackData.FilePathName.ToString() ?? "", callbackData.NamespaceVirtualizationContext),
            extendedParameters: null);
    }

    private async ValueTask<HResult> GetPlaceholderInfoCallbackAsync(string filePathName, IntPtr namespaceVirtualizationContext)
    {
        var entry = await GetEntryAsync(filePathName).ConfigureAwait(false);
        if (entry is null)
            return HResult.E_FILENOTFOUND;

        var info = new ProjFs.PRJ_PLACEHOLDER_INFO();
        info.FileBasicInfo.IsDirectory = entry.IsDirectory;
        info.FileBasicInfo.FileSize = entry.Length;

#pragma warning disable CA2000 // Dispose objects before losing scope (ownHandle: false)
        var hr = NativeMethods.PrjWritePlaceholderInfo(
                    new ProjFSSafeHandle(namespaceVirtualizationContext, ownHandle: false),
                    filePathName, // Use the full relative path from the callback, not just the entry name
                    in info,
                    (uint)Marshal.SizeOf(info));
#pragma warning restore CA2000
        return hr;
    }

    private HResult GetFileDataCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, ulong byteOffset, uint length)
    {
        return ExecuteCallback(
            in callbackData,
            GetFileDataCallbackAsync(
                callbackData.FilePathName.ToString() ?? "",
                callbackData.NamespaceVirtualizationContext,
                callbackData.DataStreamId,
                byteOffset,
                length),
            extendedParameters: null);
    }

    private async ValueTask<HResult> GetFileDataCallbackAsync(string filePathName, IntPtr namespaceVirtualizationContext, Guid dataStreamId, ulong byteOffset, uint length)
    {
        using var stream = await OpenReadAsync(filePathName).ConfigureAwait(false);
        if (stream is null)
            return HResult.E_FILENOTFOUND;

        // Seek to the requested offset
        if (byteOffset > 0)
        {
            if (stream.CanSeek)
            {
                stream.Seek((long)byteOffset, SeekOrigin.Begin);
            }
            else
            {
                // For non-seekable streams, manually read and discard bytes to advance to the offset.
                // Note: this may be slow for large offsets since all preceding bytes must be read and discarded.
                var bytesToSkip = (long)byteOffset;
                var skipBuffer = new byte[Math.Min(bytesToSkip, 4096)];
                while (bytesToSkip > 0)
                {
                    var toRead = (int)Math.Min(bytesToSkip, skipBuffer.Length);
                    var skipped = stream.Read(skipBuffer, 0, toRead);
                    if (skipped == 0)
                        break;
                    bytesToSkip -= skipped;
                }
            }
        }

        using var safeHandle = new ProjFSSafeHandle(namespaceVirtualizationContext, ownHandle: false);

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
                    dataStreamId,
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

    private static HResult ExecuteCallback(in ProjFs.PRJ_CALLBACK_DATA callbackData, ValueTask<HResult> callbackResult, ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS? extendedParameters)
    {
        if (callbackResult.IsCompletedSuccessfully)
            return callbackResult.Result;

        _ = CompleteCommandAsync(callbackData.NamespaceVirtualizationContext, callbackData.CommandId, callbackResult.AsTask(), extendedParameters);
        return HResult.ERROR_IO_PENDING;
    }

    private static async Task CompleteCommandAsync(IntPtr namespaceVirtualizationContext, int commandId, Task<HResult> callbackTask, ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS? extendedParameters)
    {
        HResult completionResult;
        try
        {
            completionResult = await callbackTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completionResult = new HResult(Marshal.GetHRForException(ex));
        }

#pragma warning disable CA2000 // Dispose objects before losing scope (ownHandle: false)
        using var instanceHandle = new ProjFSSafeHandle(namespaceVirtualizationContext, ownHandle: false);
#pragma warning restore CA2000

        var completionHr = extendedParameters is { } value
            ? NativeMethods.PrjCompleteCommandWithExtendedParameters(instanceHandle, commandId, completionResult, in value)
            : NativeMethods.PrjCompleteCommand(instanceHandle, commandId, completionResult, IntPtr.Zero);

        if (!completionHr.IsSuccess)
        {
            Debug.WriteLine($"PrjCompleteCommand failed for command {commandId}: 0x{completionHr.Value:X8}");
        }
    }

    private static ulong BlockAlignTruncate(ulong p, uint v)
    {
        // BlockAlignTruncate(): Aligns P on the previous V boundary (V must be != 0).
        // #define BlockAlignTruncate(P,V) ((P) & (0-((UINT64)(V))))
        return p & (0 - ((ulong)v));
    }
}
