using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

internal static class NativeMethods
{
    // C:\Program Files (x86)\Windows Kits\10\Include\10.0.17763.0\um\projectedfslib.h

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjMarkDirectoryAsPlaceholder(string rootPathName, string? targetPathName, IntPtr versionInfo, in Guid virtualizationInstanceID);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjStartVirtualizing(string virtualizationRootPath, in PrjCallbacks callbacks, IntPtr instanceContext, in PRJ_STARTVIRTUALIZING_OPTIONS options, out ProjFSSafeHandle namespaceVirtualizationContext);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjStopVirtualizing(IntPtr namespaceVirtualizationContext);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjFillDirEntryBuffer(string fileName, in PRJ_FILE_BASIC_INFO callbacks, IntPtr dirEntryBufferHandle);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjWritePlaceholderInfo(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int PrjFileNameCompare(string fileName1, string fileName2);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool PrjFileNameMatch(string fileNameToCheck, string pattern);

    [DllImport("ProjectedFSLib.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjGetVirtualizationInstanceInfo(ProjFSSafeHandle namespaceVirtualizationContext, out PRJ_VIRTUALIZATION_INSTANCE_INFO virtualizationInstanceInfo);

    [DllImport("ProjectedFSLib.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr PrjAllocateAlignedBuffer(ProjFSSafeHandle namespaceVirtualizationContext, uint size);

    [DllImport("ProjectedFSLib.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void PrjFreeAlignedBuffer(IntPtr buffer);

    [DllImport("ProjectedFSLib.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjWriteFileData(ProjFSSafeHandle namespaceVirtualizationContext, in Guid dataStreamId, IntPtr buffer, ulong byteOffset, uint length);

    [DllImport("ProjectedFSLib.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjClearNegativePathCache(ProjFSSafeHandle namespaceVirtualizationContext, out uint totalEntryNumber);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjGetOnDiskFileState(string destinationFileName, out PRJ_FILE_STATE fileState);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjDeleteFile(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjUpdateFileIfNeeded(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern HResult PrjCompleteCommand(ProjFSSafeHandle namespaceVirtualizationContext, int commandId, HResult completionResult, in PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS extendedParameters);

    [StructLayout(LayoutKind.Explicit)]
    internal struct PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS
    {
        [FieldOffset(0)]
        public PRJ_COMPLETE_COMMAND_TYPE CommandType;

        [FieldOffset(4)]
        public PRJ_NOTIFY_TYPES NotificationMask;

        [FieldOffset(4)]
        public IntPtr DirEntryBufferHandle;
    }

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
    internal struct PRJ_PLACEHOLDER_VERSION_INFO
    {
        public const int PRJ_PLACEHOLDER_ID_LENGTH = 128;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] ProviderID;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] ContentID;
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
    internal delegate HResult PrjGetDirectoryEnumerationCb(in PrjCallbackData callbackData, in Guid enumerationId, [MarshalAs(UnmanagedType.LPWStr), In] string searchExpression, IntPtr dirEntryBufferHandle);
    internal delegate HResult PrjEndDirectoryEnumerationCb(in PrjCallbackData callbackData, in Guid enumerationId);

    internal delegate HResult PrjGetPlaceholderInfoCb(in PrjCallbackData callbackData);
    internal delegate HResult PrjGetFileDataCb(in PrjCallbackData callbackData, ulong byteOffset, uint length);
    internal delegate HResult PrjQueryFileNameCb(in PrjCallbackData callbackData);
    internal delegate HResult PrjNotificationCb(in PrjCallbackData callbackData, bool isDirectory, PRJ_NOTIFICATION notification, [MarshalAs(UnmanagedType.LPWStr), In] string destinationFileName, [In, Out] IntPtr operationParameters);
    internal delegate HResult PrjCancelCommandCb(in PrjCallbackData callbackData);

    // Callback data passed to each of the callbacks above.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrjCallbackData
    {
        public uint Size;
        public PRJ_CALLBACK_DATA_FLAGS Flags;
        public IntPtr NamespaceVirtualizationContext;
        public int CommandId;
        public Guid FileId;
        public Guid DataStreamId;
        public string FilePathName;
        public IntPtr VersionInfo;
        public uint TriggeringProcessId;
        public string TriggeringProcessImageFileName;
        public IntPtr InstanceContext;
    }

    internal enum PRJ_CALLBACK_DATA_FLAGS : uint
    {
        PRJ_CB_DATA_FLAG_ENUM_RESTART_SCAN = 1,
        PRJ_CB_DATA_FLAG_ENUM_RETURN_SINGLE_ENTRY = 2,
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
    internal struct PRJ_STARTVIRTUALIZING_OPTIONS
    {
        public PRJ_STARTVIRTUALIZING_FLAGS Flags;
        public uint PoolThreadCount;
        public uint ConcurrentThreadCount;
        public IntPtr NotificationMappings;
        public uint NotificationMappingsCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRJ_NOTIFICATION_MAPPING
    {
        public PRJ_NOTIFY_TYPES NotificationBitMask;
        public string NotificationRoot;
    }

    internal enum PRJ_NOTIFICATION
    {
        PRJ_NOTIFICATION_FILE_OPENED = 0x00000002,
        PRJ_NOTIFICATION_NEW_FILE_CREATED = 0x00000004,
        PRJ_NOTIFICATION_FILE_OVERWRITTEN = 0x00000008,
        PRJ_NOTIFICATION_PRE_DELETE = 0x00000010,
        PRJ_NOTIFICATION_PRE_RENAME = 0x00000020,
        PRJ_NOTIFICATION_PRE_SET_HARDLINK = 0x00000040,
        PRJ_NOTIFICATION_FILE_RENAMED = 0x00000080,
        PRJ_NOTIFICATION_HARDLINK_CREATED = 0x00000100,
        PRJ_NOTIFICATION_FILE_HANDLE_CLOSED_NO_MODIFICATION = 0x00000200,
        PRJ_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_MODIFIED = 0x00000400,
        PRJ_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_DELETED = 0x00000800,
        PRJ_NOTIFICATION_FILE_PRE_CONVERT_TO_FULL = 0x00001000,
    }

    internal enum PRJ_COMPLETE_COMMAND_TYPE
    {
        PRJ_COMPLETE_COMMAND_TYPE_NOTIFICATION = 1,
        PRJ_COMPLETE_COMMAND_TYPE_ENUMERATION = 2,
    }
}
