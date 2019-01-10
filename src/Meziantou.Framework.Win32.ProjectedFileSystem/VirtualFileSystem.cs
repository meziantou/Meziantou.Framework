using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    // https://github.com/Microsoft/Windows-classic-samples/blob/master/Samples/ProjectedFileSystem/regfsProvider.cpp
    public abstract class VirtualFileSystem : IDisposable
    {
        // TODO
        // * Expose PRJ_FLAG_USE_NEGATIVE_PATH_CACHE
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjdeletefile
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjupdatefileifneeded
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjdoesnamecontainwildcards
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjcompletecommand
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjclearnegativepathcache
        // * https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/nf-projectedfslib-prjgetondiskfilestate

        private readonly Guid _virtualizationInstanceId;
        private ProjFSSafeHandle _instanceHandle;

        private readonly ConcurrentDictionary<Guid, DirectoryEnumerationSession> _activeEnumerations = new ConcurrentDictionary<Guid, DirectoryEnumerationSession>();

        public string RootFolder { get; }

        protected VirtualFileSystem(string rootFolder)
        {
            if (rootFolder == null)
                throw new ArgumentNullException(nameof(rootFolder));

            RootFolder = Path.GetFullPath(rootFolder);
            _virtualizationInstanceId = Guid.NewGuid();
        }

        public void Initialize()
        {
            if (_instanceHandle != null)
                return;

            // TODO allow options and implement notification
            var hr = NativeMethods.PrjMarkDirectoryAsPlaceholder(RootFolder, null, IntPtr.Zero, in _virtualizationInstanceId);
            hr.EnsureSuccess();

            // Set up the callback table for the projection provider.
            var callbackTable = new NativeMethods.PrjCallbacks
            {
                StartDirectoryEnumerationCallback = StartDirectoryEnumerationCallback,
                EndDirectoryEnumerationCallback = EndDirectoryEnumerationCallback,
                GetDirectoryEnumerationCallback = GetDirectoryEnumerationCallback,
                GetPlaceholderInfoCallback = GetPlaceholderInfoCallback,
                GetFileDataCallback = GetFileDataCallback,
                // TODO implements
                //CancelCommandCallback = CancelCommandCallback,
                //NotificationCallback = NotificationCallback,
                //QueryFileNameCallback = QueryFileNameCallback,
            };

            hr = NativeMethods.PrjStartVirtualizing(RootFolder, in callbackTable, IntPtr.Zero, IntPtr.Zero, out _instanceHandle);
            hr.EnsureSuccess();
        }

        public void Stop()
        {
            if (_instanceHandle == null)
                return;

            _instanceHandle.Dispose();
            _instanceHandle = null;
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
                // TODO
            }

            VirtualFileSystemEntry entry;
            while ((entry = session.GetNextEntry()) != null)
            {
                var info = new NativeMethods.PRJ_FILE_BASIC_INFO();
                info.FileSize = entry.Length;
                info.IsDirectory = entry.IsDirectory;

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
                        callbackData.NamespaceVirtualizationContext,
                        entry.Name,
                        in info,
                        (uint)Marshal.SizeOf(info));

            hr.EnsureSuccess();
            return HResult.S_OK;
        }

        private HResult GetFileDataCallback(in NativeMethods.PrjCallbackData callbackData, ulong byteOffset, uint length)
        {
            // TODO test large file
            using (var stream = OpenRead(callbackData.FilePathName))
            {
                if (stream == null)
                    return HResult.E_FILENOTFOUND;

                ulong writeStartOffset;
                uint writeLength;

                // TODO extract as property
                const uint maxBufferSize = 1024 * 1024;
                if (length <= maxBufferSize)
                {
                    // The range requested in the callback is less than the buffer size, so we can return
                    // the data in a single call, without doing any alignment calculations.
                    writeStartOffset = byteOffset;
                    writeLength = length;
                }
                else
                {
                    var hr = NativeMethods.PrjGetVirtualizationInstanceInfo(callbackData.NamespaceVirtualizationContext, out var instanceInfo);
                    if (!hr.IsSuccess)
                        return hr;

                    // The first transfer will start at the beginning of the requested range,
                    // which is guaranteed to have the correct alignment.
                    writeStartOffset = byteOffset;

                    // Ensure our transfer size is aligned to the device alignment, and is
                    // no larger than 1 MB (note this assumes the device alignment is less
                    // than 1 MB).
                    ulong writeEndOffset = BlockAlignTruncate(writeStartOffset + maxBufferSize, instanceInfo.WriteAlignment);
                    Debug.Assert(writeEndOffset > 0);
                    Debug.Assert(writeEndOffset > writeStartOffset);

                    writeLength = (uint)(writeEndOffset - writeStartOffset);
                }

                // Allocate a buffer that adheres to the needed memory alignment.
                IntPtr writeBuffer = NativeMethods.PrjAllocateAlignedBuffer(callbackData.NamespaceVirtualizationContext, writeLength);
                if (writeBuffer == IntPtr.Zero)
                    return HResult.E_OUTOFMEMORY;

                do
                {
                    byte[] data = new byte[writeLength];
                    var read = stream.Read(data, 0, data.Length);

                    Marshal.Copy(data, 0, IntPtr.Add(writeBuffer, (int)writeStartOffset), read);

                    // Write the data to the file in the local file system.
                    var hr = NativeMethods.PrjWriteFileData(callbackData.NamespaceVirtualizationContext,
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
        }

        private static ulong BlockAlignTruncate(ulong p, uint v)
        {
            // BlockAlignTruncate(): Aligns P on the previous V boundary (V must be != 0).
            // #define BlockAlignTruncate(P,V) ((P) & (0-((UINT64)(V))))
            return p & (0 - ((ulong)v));
        }

        protected static bool FileNameMatch(string fileName, string pattern)
        {
            return NativeMethods.PrjFileNameMatch(fileName, pattern);
        }

        protected static int CompareFileName(string fileName1, string fileName2)
        {
            return FileNameComparer.Instance.Compare(fileName1, fileName2);
        }

        // TODO handle not found
        protected abstract IEnumerable<VirtualFileSystemEntry> GetEntries(string path);

        // TODO handle not found
        protected abstract VirtualFileSystemEntry GetEntry(string path);

        protected abstract Stream OpenRead(string path);

        public void Dispose()
        {
            Stop();
        }
    }
}
