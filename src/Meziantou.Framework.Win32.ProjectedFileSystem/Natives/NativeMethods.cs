using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using ProjFs = Windows.Win32.Storage.ProjectedFileSystem;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

[SupportedOSPlatform("windows10.0.17763")]
internal static class NativeMethods
{
    internal static HResult PrjMarkDirectoryAsPlaceholder(string rootPathName, string? targetPathName, IntPtr versionInfo, in Guid virtualizationInstanceID)
    {
        if (versionInfo != IntPtr.Zero)
            throw new NotSupportedException($"'{nameof(versionInfo)}' must be {nameof(IntPtr)}.{nameof(IntPtr.Zero)}");

        return ToHResult(PInvoke.PrjMarkDirectoryAsPlaceholder(rootPathName, targetPathName, versionInfo: null, in virtualizationInstanceID));
    }

    internal static unsafe HResult PrjStartVirtualizing(string virtualizationRootPath, in ProjFs.PRJ_CALLBACKS callbacks, IntPtr instanceContext, in ProjFs.PRJ_STARTVIRTUALIZING_OPTIONS options, out ProjFSSafeHandle namespaceVirtualizationContext)
    {
        var hr = ToHResult(PInvoke.PrjStartVirtualizing(virtualizationRootPath, in callbacks, (void*)instanceContext, options, out var context));
        namespaceVirtualizationContext = new ProjFSSafeHandle((IntPtr)context, ownHandle: true);
        return hr;
    }

    internal static HResult PrjStopVirtualizing(IntPtr namespaceVirtualizationContext)
    {
        PInvoke.PrjStopVirtualizing((ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT)namespaceVirtualizationContext);
        return HResult.S_OK;
    }

    internal static HResult PrjFillDirEntryBuffer(string fileName, in ProjFs.PRJ_FILE_BASIC_INFO callbacks, IntPtr dirEntryBufferHandle)
    {
        return ToHResult(PInvoke.PrjFillDirEntryBuffer(fileName, callbacks, (ProjFs.PRJ_DIR_ENTRY_BUFFER_HANDLE)dirEntryBufferHandle));
    }

    internal static unsafe HResult PrjWritePlaceholderInfo(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in ProjFs.PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize)
    {
        fixed (ProjFs.PRJ_PLACEHOLDER_INFO* placeholderInfoPointer = &placeholderInfo)
        {
            var placeholderInfoData = new ReadOnlySpan<byte>((byte*)placeholderInfoPointer, checked((int)placeholderInfoSize));
            return ToHResult(PInvoke.PrjWritePlaceholderInfo(ToContext(namespaceVirtualizationContext), destinationFileName, placeholderInfoData));
        }
    }

    internal static int PrjFileNameCompare(string fileName1, string fileName2)
    {
        return PInvoke.PrjFileNameCompare(fileName1, fileName2);
    }

    internal static bool PrjFileNameMatch(string fileNameToCheck, string pattern)
    {
        return PInvoke.PrjFileNameMatch(fileNameToCheck, pattern);
    }

    internal static HResult PrjGetVirtualizationInstanceInfo(ProjFSSafeHandle namespaceVirtualizationContext, out ProjFs.PRJ_VIRTUALIZATION_INSTANCE_INFO virtualizationInstanceInfo)
    {
        return ToHResult(PInvoke.PrjGetVirtualizationInstanceInfo(ToContext(namespaceVirtualizationContext), out virtualizationInstanceInfo));
    }

    internal static unsafe IntPtr PrjAllocateAlignedBuffer(ProjFSSafeHandle namespaceVirtualizationContext, uint size)
    {
        return (IntPtr)PInvoke.PrjAllocateAlignedBuffer(ToContext(namespaceVirtualizationContext), (nuint)size);
    }

    internal static unsafe void PrjFreeAlignedBuffer(IntPtr buffer)
    {
        PInvoke.PrjFreeAlignedBuffer((void*)buffer);
    }

    internal static unsafe HResult PrjWriteFileData(ProjFSSafeHandle namespaceVirtualizationContext, in Guid dataStreamId, IntPtr buffer, ulong byteOffset, uint length)
    {
        var payload = new ReadOnlySpan<byte>((void*)buffer, checked((int)length));
        return ToHResult(PInvoke.PrjWriteFileData(ToContext(namespaceVirtualizationContext), in dataStreamId, payload, byteOffset));
    }

    internal static HResult PrjClearNegativePathCache(ProjFSSafeHandle namespaceVirtualizationContext, out uint totalEntryNumber)
    {
        return ToHResult(PInvoke.PrjClearNegativePathCache(ToContext(namespaceVirtualizationContext), out totalEntryNumber));
    }

    internal static HResult PrjGetOnDiskFileState(string destinationFileName, out PRJ_FILE_STATE fileState)
    {
        var hr = ToHResult(PInvoke.PrjGetOnDiskFileState(destinationFileName, out var nativeFileState));
        fileState = (PRJ_FILE_STATE)nativeFileState;
        return hr;
    }

    internal static HResult PrjDeleteFile(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
    {
        var hr = ToHResult(PInvoke.PrjDeleteFile(ToContext(namespaceVirtualizationContext), destinationFileName, (ProjFs.PRJ_UPDATE_TYPES)updateFlags, out var nativeFailureReason));
        failureReason = (PRJ_UPDATE_FAILURE_CAUSES)nativeFailureReason;
        return hr;
    }

    internal static unsafe HResult PrjUpdateFileIfNeeded(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in ProjFs.PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
    {
        ProjFs.PRJ_UPDATE_FAILURE_CAUSES nativeFailureReason;
        fixed (ProjFs.PRJ_PLACEHOLDER_INFO* placeholderInfoPointer = &placeholderInfo)
        {
            var placeholderInfoData = new ReadOnlySpan<byte>((byte*)placeholderInfoPointer, checked((int)placeholderInfoSize));
            var hr = ToHResult(PInvoke.PrjUpdateFileIfNeeded(ToContext(namespaceVirtualizationContext), destinationFileName, placeholderInfoData, (ProjFs.PRJ_UPDATE_TYPES)updateFlags, out nativeFailureReason));
            failureReason = (PRJ_UPDATE_FAILURE_CAUSES)nativeFailureReason;
            return hr;
        }
    }

    internal static HResult PrjCompleteCommand(ProjFSSafeHandle namespaceVirtualizationContext, int commandId, HResult completionResult, IntPtr extendedParameters)
    {
        ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS? parameters = extendedParameters == IntPtr.Zero
            ? null
            : Marshal.PtrToStructure<ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS>(extendedParameters);
        return ToHResult(PInvoke.PrjCompleteCommand(ToContext(namespaceVirtualizationContext), commandId, ToHRESULT(completionResult), parameters));
    }

    internal static HResult PrjCompleteCommandWithExtendedParameters(ProjFSSafeHandle namespaceVirtualizationContext, int commandId, HResult completionResult, in ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS extendedParameters)
    {
        return ToHResult(PInvoke.PrjCompleteCommand(ToContext(namespaceVirtualizationContext), commandId, ToHRESULT(completionResult), extendedParameters));
    }

    private static ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT ToContext(ProjFSSafeHandle handle)
    {
        return (ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT)handle.DangerousGetHandle();
    }

    private static HResult ToHResult(HRESULT value)
    {
        return new HResult(value.Value);
    }

    private static HRESULT ToHRESULT(HResult value)
    {
        return (HRESULT)value.Value;
    }
}
