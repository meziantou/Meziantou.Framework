using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    internal static class NativeMethods
    {
        // C:\Program Files (x86)\Windows Kits\10\Include\10.0.17763.0\um\projectedfslib.h

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern HResult PrjMarkDirectoryAsPlaceholder(string rootPathName, string targetPathName, IntPtr versionInfo, in Guid virtualizationInstanceID);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern HResult PrjStartVirtualizing(string virtualizationRootPath, in PrjCallbacks callbacks, IntPtr instanceContext, IntPtr options, out ProjFSSafeHandle namespaceVirtualizationContext);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern void PrjStopVirtualizing(IntPtr namespaceVirtualizationContext);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern HResult PrjFillDirEntryBuffer(string fileName, in PRJ_FILE_BASIC_INFO callbacks, IntPtr dirEntryBufferHandle);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern HResult PrjWritePlaceholderInfo(IntPtr namespaceVirtualizationContext, string destinationFileName, in PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern int PrjFileNameCompare(string fileName1, string fileName2);

        [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
        internal static extern bool PrjFileNameMatch(string fileNameToCheck, string pattern);

        [DllImport("ProjectedFSLib.dll")]
        internal static extern HResult PrjGetVirtualizationInstanceInfo(IntPtr namespaceVirtualizationContext, out PRJ_VIRTUALIZATION_INSTANCE_INFO virtualizationInstanceInfo);

        [DllImport("ProjectedFSLib.dll")]
        internal static extern IntPtr PrjAllocateAlignedBuffer(IntPtr namespaceVirtualizationContext, uint size);

        [DllImport("ProjectedFSLib.dll")]
        internal static extern void PrjFreeAlignedBuffer(IntPtr buffer);

        [DllImport("ProjectedFSLib.dll")]
        internal static extern HResult PrjWriteFileData(IntPtr namespaceVirtualizationContext, in Guid dataStreamId, IntPtr buffer, ulong byteOffset, uint length);

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct PRJ_PLACEHOLDER_INFO
        {
            public PRJ_FILE_BASIC_INFO FileBasicInfo;
            public EaInformation EaInformation;
            public SecurityInformation SecurityInformation;
            public StreamsInformation StreamsInformation;
            public PRJ_PLACEHOLDER_VERSION_INFO VersionInfo;
            public byte VariableData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EaInformation
        {
            public uint EaBufferSize;
            public uint OffsetToFirstEa;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityInformation
        {
            public uint SecurityBufferSize;
            public uint OffsetToSecurityDescriptor;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StreamsInformation
        {
            public uint StreamsInfoBufferSize;
            public uint OffsetToFirstStreamInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct PRJ_PLACEHOLDER_VERSION_INFO
        {
            public fixed byte ProviderID[128];
            public fixed byte ContentID[128];
        }

        // Structure configuring the projection provider callbacks.
        [StructLayout(LayoutKind.Sequential)]
        internal struct PrjCallbacks
        {
            public PrjStartDirectoryEnumerationCb StartDirectoryEnumerationCallback;
            public PrjEndDirectoryEnumerationCb EndDirectoryEnumerationCallback;
            public PrjGetDirectoryEnumerationCb GetDirectoryEnumerationCallback;
            public PrjGetPlaceholderInfoCb GetPlaceholderInfoCallback;
            public PrjGetFileDataCb GetFileDataCallback;
            public PrjQueryFileNameCb QueryFileNameCallback;
            public PrjNotificationCb NotificationCallback;
            public PrjCancelCommandCb CancelCommandCallback;
        }

        // Callback signatures.
        internal delegate HResult PrjStartDirectoryEnumerationCb(in PrjCallbackData callbackData, in Guid enumerationId);
        internal delegate HResult PrjGetDirectoryEnumerationCb(in PrjCallbackData callbackData, in Guid enumerationId, [MarshalAs(UnmanagedType.LPWStr), In]string searchExpression, IntPtr dirEntryBufferHandle);
        internal delegate HResult PrjEndDirectoryEnumerationCb(in PrjCallbackData callbackData, in Guid enumerationId);

        internal delegate HResult PrjGetPlaceholderInfoCb(in PrjCallbackData callbackData);
        internal delegate HResult PrjGetFileDataCb(in PrjCallbackData callbackData, ulong byteOffset, uint length);
        internal delegate HResult PrjQueryFileNameCb(in PrjCallbackData callbackData);
        internal delegate HResult PrjNotificationCb(in PrjCallbackData callbackData, bool isDirectory, int notification /* TODO */, [MarshalAs(UnmanagedType.LPWStr), In]string destinationFileName, IntPtr operationParameters);
        internal delegate HResult PrjCancelCommandCb(in PrjCallbackData callbackData);

        // Callback data passed to each of the callbacks above.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PrjCallbackData
        {
            public uint Size;
            public PRJ_CALLBACK_DATA_FLAGS Flags;
            public IntPtr NamespaceVirtualizationContext; // TODO
            public int CommandId;
            public Guid FileId;
            public Guid DataStreamId;
            public string FilePathName;
            public IntPtr VersionInfo; // TODO
            public uint TriggeringProcessId;
            public string TriggeringProcessImageFileName;
            public IntPtr InstanceContext;
        }

        internal enum PRJ_CALLBACK_DATA_FLAGS : uint
        {
            PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN,
            PRJ_CB_DATA_FLAG_ENUM_RETURN_SINGLE_ENTRY
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PRJ_FILE_BASIC_INFO
        {
            public bool IsDirectory;
            public long FileSize;
            public LARGE_INTEGER CreationTime;
            public LARGE_INTEGER LastAccessTime;
            public LARGE_INTEGER LastWriteTime;
            public LARGE_INTEGER ChangeTime;
            public FileAttributes FileAttributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PRJ_VIRTUALIZATION_INSTANCE_INFO
        {
            public Guid InstanceID;
            public uint WriteAlignment;
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        internal struct LARGE_INTEGER
        {
            [FieldOffset(0)] public long QuadPart;
            [FieldOffset(0)] public uint LowPart;
            [FieldOffset(4)] public int HighPart;
        }

        internal enum PRJ_STARTVIRTUALIZING_FLAGS
        {
            PRJ_FLAG_NONE,
            PRJ_FLAG_USE_NEGATIVE_PATH_CACHE,
        };

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct PRJ_STARTVIRTUALIZING_OPTIONS
        {
            public PRJ_STARTVIRTUALIZING_FLAGS Flags;
            public uint PoolThreadCount;
            public uint ConcurrentThreadCount;
            public IntPtr NotificationMappings; // PRJ_NOTIFICATION_MAPPING
            public uint NotificationMappingsCount;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PRJ_NOTIFICATION_MAPPING
        {
            public PRJ_NOTIFY_TYPES NotificationBitMask;
            public string NotificationRoot;
        }

        internal enum PRJ_NOTIFY_TYPES
        {
            PRJ_NOTIFY_NONE,
            PRJ_NOTIFY_SUPPRESS_NOTIFICATIONS,
            PRJ_NOTIFY_FILE_OPENED,
            PRJ_NOTIFY_NEW_FILE_CREATED,
            PRJ_NOTIFY_FILE_OVERWRITTEN,
            PRJ_NOTIFY_PRE_DELETE,
            PRJ_NOTIFY_PRE_RENAME,
            PRJ_NOTIFY_PRE_SET_HARDLINK,
            PRJ_NOTIFY_FILE_RENAMED,
            PRJ_NOTIFY_HARDLINK_CREATED,
            PRJ_NOTIFY_FILE_HANDLE_CLOSED_NO_MODIFICATION,
            PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_MODIFIED,
            PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_DELETED,
            PRJ_NOTIFY_FILE_PRE_CONVERT_TO_FULL,
            PRJ_NOTIFY_USE_EXISTING_MASK,
        }
    }
}
