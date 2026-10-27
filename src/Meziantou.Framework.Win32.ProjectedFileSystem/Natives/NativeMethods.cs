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

    internal static unsafe HResult PrjStartVirtualizing(string virtualizationRootPath, in ProjFs.PRJ_CALLBACKS callbacks, IntPtr instanceContext, in ProjFs.PRJ_STARTVIRTUALIZING_OPTIONS options, out ProjFSSafeHandle? namespaceVirtualizationContext)
    {
        var hr = ToHResult(PInvoke.PrjStartVirtualizing(virtualizationRootPath, in callbacks, (void*)instanceContext, options, out var context));

        // Only take ownership when the call succeeded. On failure the out parameter is not a
        // handle the driver ever gave out, and wrapping it leaves the instance looking started.
        namespaceVirtualizationContext = hr.IsSuccess ? new ProjFSSafeHandle((IntPtr)context, ownHandle: true) : null;
        return hr;
    }

    // PrjStopVirtualizing has no return value, so there is no status to surface here
    internal static void PrjStopVirtualizing(IntPtr namespaceVirtualizationContext)
    {
        PInvoke.PrjStopVirtualizing((ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT)namespaceVirtualizationContext);
    }

    internal static HResult PrjFillDirEntryBuffer(string fileName, in ProjFs.PRJ_FILE_BASIC_INFO callbacks, IntPtr dirEntryBufferHandle)
    {
        return ToHResult(PInvoke.PrjFillDirEntryBuffer(fileName, callbacks, (ProjFs.PRJ_DIR_ENTRY_BUFFER_HANDLE)dirEntryBufferHandle));
    }

    internal static unsafe HResult PrjWritePlaceholderInfo(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in ProjFs.PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        fixed (ProjFs.PRJ_PLACEHOLDER_INFO* placeholderInfoPointer = &placeholderInfo)
        {
            var placeholderInfoData = new ReadOnlySpan<byte>((byte*)placeholderInfoPointer, checked((int)placeholderInfoSize));
            return ToHResult(PInvoke.PrjWritePlaceholderInfo(ToContext(context), destinationFileName, placeholderInfoData));
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
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        return ToHResult(PInvoke.PrjGetVirtualizationInstanceInfo(ToContext(context), out virtualizationInstanceInfo));
    }

    internal static unsafe IntPtr PrjAllocateAlignedBuffer(ProjFSSafeHandle namespaceVirtualizationContext, uint size)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        return (IntPtr)PInvoke.PrjAllocateAlignedBuffer(ToContext(context), (nuint)size);
    }

    internal static unsafe void PrjFreeAlignedBuffer(IntPtr buffer)
    {
        PInvoke.PrjFreeAlignedBuffer((void*)buffer);
    }

    internal static unsafe HResult PrjWriteFileData(ProjFSSafeHandle namespaceVirtualizationContext, in Guid dataStreamId, IntPtr buffer, ulong byteOffset, uint length)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        var payload = new ReadOnlySpan<byte>((void*)buffer, checked((int)length));
        return ToHResult(PInvoke.PrjWriteFileData(ToContext(context), in dataStreamId, payload, byteOffset));
    }

    internal static HResult PrjClearNegativePathCache(ProjFSSafeHandle namespaceVirtualizationContext, out uint totalEntryNumber)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        return ToHResult(PInvoke.PrjClearNegativePathCache(ToContext(context), out totalEntryNumber));
    }

    internal static HResult PrjGetOnDiskFileState(string destinationFileName, out PRJ_FILE_STATE fileState)
    {
        var hr = ToHResult(PInvoke.PrjGetOnDiskFileState(destinationFileName, out var nativeFileState));
        fileState = (PRJ_FILE_STATE)nativeFileState;
        return hr;
    }

    internal static HResult PrjDeleteFile(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        var hr = ToHResult(PInvoke.PrjDeleteFile(ToContext(context), destinationFileName, (ProjFs.PRJ_UPDATE_TYPES)updateFlags, out var nativeFailureReason));
        failureReason = (PRJ_UPDATE_FAILURE_CAUSES)nativeFailureReason;
        return hr;
    }

    internal static unsafe HResult PrjUpdateFileIfNeeded(ProjFSSafeHandle namespaceVirtualizationContext, string destinationFileName, in ProjFs.PRJ_PLACEHOLDER_INFO placeholderInfo, uint placeholderInfoSize, PRJ_UPDATE_TYPES updateFlags, out PRJ_UPDATE_FAILURE_CAUSES failureReason)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        ProjFs.PRJ_UPDATE_FAILURE_CAUSES nativeFailureReason;
        fixed (ProjFs.PRJ_PLACEHOLDER_INFO* placeholderInfoPointer = &placeholderInfo)
        {
            var placeholderInfoData = new ReadOnlySpan<byte>((byte*)placeholderInfoPointer, checked((int)placeholderInfoSize));
            var hr = ToHResult(PInvoke.PrjUpdateFileIfNeeded(ToContext(context), destinationFileName, placeholderInfoData, (ProjFs.PRJ_UPDATE_TYPES)updateFlags, out nativeFailureReason));
            failureReason = (PRJ_UPDATE_FAILURE_CAUSES)nativeFailureReason;
            return hr;
        }
    }

    internal static HResult PrjCompleteCommand(ProjFSSafeHandle namespaceVirtualizationContext, int commandId, HResult completionResult, IntPtr extendedParameters)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS? parameters = extendedParameters == IntPtr.Zero
            ? null
            : Marshal.PtrToStructure<ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS>(extendedParameters);
        return ToHResult(PInvoke.PrjCompleteCommand(ToContext(context), commandId, ToHRESULT(completionResult), parameters));
    }

    internal static HResult PrjCompleteCommandWithExtendedParameters(ProjFSSafeHandle namespaceVirtualizationContext, int commandId, HResult completionResult, in ProjFs.PRJ_COMPLETE_COMMAND_EXTENDED_PARAMETERS extendedParameters)
    {
        using var context = new SafeHandleValue(namespaceVirtualizationContext);
        return ToHResult(PInvoke.PrjCompleteCommand(ToContext(context), commandId, ToHRESULT(completionResult), extendedParameters));
    }

    // The callers hold a reference on the handle for the duration of the native call: Stop can dispose the handle
    // concurrently, and the driver must not receive a context that PrjStopVirtualizing already tore down
    private static ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT ToContext(SafeHandleValue handle)
    {
        return (ProjFs.PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT)handle.Value;
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
