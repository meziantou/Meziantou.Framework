using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Represents the memory accounting information for a job object.</summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record JobObjectMemoryAccountingInformation
{
    /// <summary>The peak memory used by any process ever associated with the job.</summary>
    public required ulong PeakProcessMemoryUsed { get; init; }

    /// <summary>The peak memory usage of all processes currently associated with the job.</summary>
    public required ulong PeakJobMemoryUsed { get; init; }
}