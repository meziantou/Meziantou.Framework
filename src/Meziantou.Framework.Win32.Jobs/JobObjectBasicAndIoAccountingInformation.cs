using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Contains basic and io accounting information for a job object.</summary>
public sealed record JobObjectBasicAndIoAccountingInformation
{
    /// <summary>Contains I/O accounting information for a process or a job object.</summary>
    public required JobObjectBasicAccountingInformation BasicInfo { get; init; }

    /// <summary>Contains I/O counters for the job object.</summary>
    public required JobObjectIoCounters IoInfo { get; init; }
}