using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from the <c>GetLogicalProcessorInformationEx</c> Win32 API and the
/// <c>HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor</c> registry key.
/// It replaces the <c>wmic</c> command line tool which is being removed from Windows.
/// Windows only.
/// </summary>
internal static class WindowsCpuInfoProvider
{
    private const string CentralProcessorKeyName = @"HARDWARE\DESCRIPTION\System\CentralProcessor";
    private const string ProcessorNameValueName = "ProcessorNameString";
    private const string ClockSpeedValueName = "~MHz";

    internal static readonly Lazy<CpuInfo?> WindowsCpuInfo = new(Load);

    private static CpuInfo? Load()
    {
        // GetLogicalProcessorInformationEx requires Windows 7 / Windows Server 2008 R2
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return null;

        if (!TryGetProcessorCounts(out var physicalProcessorCount, out var physicalCoreCount, out var logicalCoreCount))
            return MosCpuInfoProvider.MosCpuInfo.Value;

        var (processorName, maxClockSpeed) = GetProcessorNameAndClockSpeed();

        return new CpuInfo(
            processorName,
            physicalProcessorCount > 0 ? physicalProcessorCount : null,
            physicalCoreCount > 0 ? physicalCoreCount : null,
            logicalCoreCount > 0 ? logicalCoreCount : null,
            nominalFrequency: null,
            minFrequency: null,
            maxClockSpeed > 0 ? Frequency.FromMHz(maxClockSpeed) : null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static unsafe bool TryGetProcessorCounts(out int physicalProcessorCount, out int physicalCoreCount, out int logicalCoreCount)
    {
        physicalProcessorCount = 0;
        physicalCoreCount = 0;
        logicalCoreCount = 0;

        uint length = 0;
        if (PInvoke.GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, Buffer: null, &length) ||
            (WIN32_ERROR)Marshal.GetLastPInvokeError() is not WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER ||
            length is 0)
        {
            return false;
        }

        var buffer = (byte*)NativeMemory.AlignedAlloc(length, (nuint)sizeof(nuint));
        try
        {
            if (!PInvoke.GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)buffer, &length))
                return false;

            CountProcessors(buffer, length, out physicalProcessorCount, out physicalCoreCount, out logicalCoreCount);
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }

        return true;
    }

    internal static unsafe void CountProcessors(byte* buffer, uint length, out int physicalProcessorCount, out int physicalCoreCount, out int logicalCoreCount)
    {
        physicalProcessorCount = 0;
        physicalCoreCount = 0;
        logicalCoreCount = 0;

        // The entries have a variable size, so they must be walked using the Size member of their common header
        var headerSize = (uint)(sizeof(LOGICAL_PROCESSOR_RELATIONSHIP) + sizeof(uint));
        for (var offset = 0u; offset + headerSize <= length;)
        {
            var info = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)(buffer + offset);
            if (info->Size < headerSize || info->Size > length - offset)
                break;

            switch (info->Relationship)
            {
                case LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorPackage:
                    physicalProcessorCount++;
                    break;

                case LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore:
                    physicalCoreCount++;
                    logicalCoreCount += CountLogicalProcessors(ref info->Anonymous.Processor);
                    break;
            }

            offset += info->Size;
        }
    }

    private static int CountLogicalProcessors(ref PROCESSOR_RELATIONSHIP processor)
    {
        var count = 0;
        foreach (var groupMask in processor.GroupMask.AsSpan(processor.GroupCount))
        {
            count += BitOperations.PopCount(groupMask.Mask);
        }

        return count;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (string? ProcessorName, int MaxClockSpeed) GetProcessorNameAndClockSpeed()
    {
        var processorNames = new HashSet<string>(StringComparer.Ordinal);
        var maxClockSpeed = 0;

        try
        {
            using var centralProcessorKey = Registry.LocalMachine.OpenSubKey(CentralProcessorKeyName);
            if (centralProcessorKey is not null)
            {
                foreach (var subKeyName in centralProcessorKey.GetSubKeyNames())
                {
                    using var processorKey = centralProcessorKey.OpenSubKey(subKeyName);
                    if (processorKey is null)
                        continue;

                    if (processorKey.GetValue(ProcessorNameValueName) is string name && !string.IsNullOrWhiteSpace(name))
                        processorNames.Add(name.Trim());

                    if (processorKey.GetValue(ClockSpeedValueName) is int clockSpeed && clockSpeed > maxClockSpeed)
                        maxClockSpeed = clockSpeed;
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return (processorNames.Count > 0 ? string.Join(", ", processorNames) : null, maxClockSpeed);
    }
}
