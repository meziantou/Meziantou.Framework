using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    // https://github.com/Microsoft/Windows-classic-samples/blob/master/Samples/ProjectedFileSystem/regfsProvider.cpp
    public abstract class ProjectedFileSystemBase : IDisposable
    {
        // TODO
        // * Version
        // * FileNotFound
        // * Async loading (https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjcompletecommand)
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjupdatefileifneeded

        private readonly Guid _virtualizationInstanceId;
        private ProjFSSafeHandle? _instanceHandle;

        private readonly ConcurrentDictionary<Guid, DirectoryEnumerationSession> _activeEnumerations = new ConcurrentDictionary<Guid, DirectoryEnumerationSession>();
        private long _context;

        public string RootFolder { get; }

        protected int BufferSize { get; set; } = 4096; // 4kB

        protected ProjectedFileSystemBase(string rootFolder)
        {
            if (!Environment.Is64BitProcess)
                throw new NotSupportedException("Projected File System is only supported on 64-bit process");

            if (rootFolder == null)
                throw new ArgumentNullException(nameof(rootFolder));

            RootFolder = Path.GetFullPath(rootFolder);
            _virtualizationInstanceId = Guid.NewGuid();
        }

        public void Start(ProjectedFileSystemStartOptions? options)
        {
            if (_instanceHandle != null)
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
            var callbackTable = new NativeMethods.PrjCallbacks
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
                if (false && options != null)
                {
                    opt.Flags = options.UseNegativePathCache ? NativeMethods.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_USE_NEGATIVE_PATH_CACHE : NativeMethods.PRJ_STARTVIRTUALIZING_FLAGS.PRJ_FLAG_NONE;

                    // TODO extract to function
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
#pragma warning disable IDE0067 // Dispose objects before losing scope: _instanceHandle is disposed in Dispose
                var hr = NativeMethods.PrjStartVirtualizing(RootFolder, in callbackTable, new IntPtr(context), in opt, out _instanceHandle);
#pragma warning restore IDE0067
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

        public static PRJ_FILE_STATE GetOnDiskFileState(string path)
        {
            var hr = NativeMethods.PrjGetOnDiskFileState(path, out var state);
            hr.EnsureSuccess();
            return state;
        }

        protected void ClearNegativePathCache()
        {
            if (_instanceHandle == null)
                throw new InvalidOperationException("The service is not started");

            var result = NativeMethods.PrjClearNegativePathCache(_instanceHandle, out _);
            result.EnsureSuccess();
        }

        protected bool DeleteFile(string relativePath, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
        {
            if (_instanceHandle == null)
                throw new InvalidOperationException("The service is not started");

            var hr = NativeMethods.PrjDeleteFile(_instanceHandle, relativePath, updateFlags, out failureReason);
            if (hr == HResult.ERROR_FILE_SYSTEM_VIRTUALIZATION_INVALID_OPERATION)
                return false;

            hr.EnsureSuccess();
            return true;
        }

        public void Stop()
        {
            if (_instanceHandle == null)
                return;

            _instanceHandle.Dispose();
            _instanceHandle = null;
        }

        protected static bool FileNameMatch(string fileName, string pattern)
        {
            return NativeMethods.PrjFileNameMatch(fileName, pattern);
        }

        protected static bool AreFileNamesEqual(string fileName1, string fileName2)
        {
            return CompareFileName(fileName1, fileName2) == 0;
        }

        protected static int CompareFileName(string fileName1, string fileName2)
        {
            return FileNameComparer.Instance.Compare(fileName1, fileName2);
        }

        protected abstract IEnumerable<ProjectedFileSystemEntry> GetEntries(string path);

        protected virtual ProjectedFileSystemEntry GetEntry(string path)
        {
            var directory = Path.GetDirectoryName(path);
            return GetEntries(directory ?? "").FirstOrDefault(entry => CompareFileName(entry.Name, path) == 0);
        }

        protected abstract Stream OpenRead(string path);

        public void Dispose()
        {
            Stop();
        }

        private HResult QueryFileNameCallback(in NativeMethods.PrjCallbackData callbackData)
        {
            var fileName = callbackData.FilePathName;
            if (GetEntry(fileName) != null)
                return HResult.S_OK;

            return HResult.E_FILENOTFOUND;
        }

        private static HResult NotificationCallback(in NativeMethods.PrjCallbackData callbackData, bool isDirectory, NativeMethods.PRJ_NOTIFICATION notification, string destinationFileName, IntPtr operationParameters /*TODO*/)
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
            // TODO be able to return directory not found
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
            while ((entry = session.GetNextEntry()) != null)
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
            if (entry == null)
                return HResult.E_FILENOTFOUND;

            var info = new NativeMethods.PRJ_PLACEHOLDER_INFO();
            info.FileBasicInfo.IsDirectory = entry.IsDirectory;
            info.FileBasicInfo.FileSize = entry.Length;

            var hr = NativeMethods.PrjWritePlaceholderInfo(
                        new ProjFSSafeHandle(callbackData.NamespaceVirtualizationContext, ownHandle: false),
                        entry.Name,
                        in info,
                        (uint)Marshal.SizeOf(info));

            hr.EnsureSuccess();
            return HResult.S_OK;
        }

        private HResult GetFileDataCallback(in NativeMethods.PrjCallbackData callbackData, ulong byteOffset, uint length)
        {
            using var stream = OpenRead(callbackData.FilePathName);
            if (stream == null)
                return HResult.E_FILENOTFOUND;

            ulong writeStartOffset;
            uint writeLength;

            var safeHandle = new ProjFSSafeHandle(callbackData.NamespaceVirtualizationContext, ownHandle: false);

            var maxBufferSize = (uint)BufferSize;
            if (length <= maxBufferSize)
            {
                // The range requested in the callback is less than the buffer size, so we can return
                // the data in a single call, without doing any alignment calculations.
                writeStartOffset = byteOffset;
                writeLength = length;
            }
            else
            {
                var hr = NativeMethods.PrjGetVirtualizationInstanceInfo(safeHandle, out var instanceInfo);
                if (!hr.IsSuccess)
                    return hr;

                // The first transfer will start at the beginning of the requested range,
                // which is guaranteed to have the correct alignment.
                writeStartOffset = byteOffset;

                // Ensure our transfer size is aligned to the device alignment, and is
                // no larger than buffer size (note this assumes the device alignment is less than buffer size).
                ulong writeEndOffset = BlockAlignTruncate(writeStartOffset + maxBufferSize, instanceInfo.WriteAlignment);
                Debug.Assert(writeEndOffset > 0);
                Debug.Assert(writeEndOffset > writeStartOffset);

                writeLength = (uint)(writeEndOffset - writeStartOffset);
            }

            // Allocate a buffer that adheres to the needed memory alignment.
            var writeBuffer = NativeMethods.PrjAllocateAlignedBuffer(safeHandle, writeLength);
            if (writeBuffer == IntPtr.Zero)
                return HResult.E_OUTOFMEMORY;

            var data = new byte[writeLength];
            do
            {
                var read = stream.Read(data, 0, data.Length);

                Marshal.Copy(data, 0, IntPtr.Add(writeBuffer, (int)writeStartOffset), read);

                // Write the data to the file in the local file system.
                var hr = NativeMethods.PrjWriteFileData(safeHandle,
                                      callbackData.DataStreamId,
                                      writeBuffer,
                                      writeStartOffset,
                                      writeLength);

                if (!hr.IsSuccess)
                {
                    NativeMethods.PrjFreeAlignedBuffer(writeBuffer);
                    return hr;
                }

                // The length parameter to the callback is guaranteed to be either
                // correctly aligned or to result in a write to the end of the file.
                length -= writeLength;
                if (length < writeLength)
                {
                    writeLength = length;
                }
            }
            while (writeLength > 0);

            NativeMethods.PrjFreeAlignedBuffer(writeBuffer);
            return HResult.S_OK;
        }

        private static ulong BlockAlignTruncate(ulong p, uint v)
        {
            // BlockAlignTruncate(): Aligns P on the previous V boundary (V must be != 0).
            // #define BlockAlignTruncate(P,V) ((P) & (0-((UINT64)(V))))
            return p & (0 - ((ulong)v));
        }
    }
}
