using System.Collections.Immutable;
using System.Diagnostics;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a process at a specific point in time.</summary>
public class ProcessSnapshot
{
    internal ProcessSnapshot(Process process)
    {
        Id = process.Id;
        ProcessName = process.ProcessName;
        StartTime = process.StartTime;

        MainModule = process.MainModule is null ? null : new ProcessModuleSnapshot(process.MainModule);
        Modules = process.Modules.Cast<ProcessModule>().Select(module => new ProcessModuleSnapshot(module)).ToImmutableArray();

        UserProcessorTime = process.UserProcessorTime;
        TotalProcessorTime = process.TotalProcessorTime;
        PrivilegedProcessorTime = process.PrivilegedProcessorTime;

        HandleCount = process.HandleCount;

        WorkingSet64 = process.WorkingSet64;
        VirtualMemorySize64 = process.VirtualMemorySize64;
        PrivateMemorySize64 = process.PrivateMemorySize64;
        PagedMemorySize64 = process.PagedMemorySize64;
        PagedSystemMemorySize64 = process.PagedSystemMemorySize64;
        NonpagedSystemMemorySize64 = process.NonpagedSystemMemorySize64;

        PeakWorkingSet64 = process.PeakWorkingSet64;
        PeakVirtualMemorySize64 = process.PeakVirtualMemorySize64;
        PeakPagedMemorySize64 = process.PeakPagedMemorySize64;
        PriorityClass = process.PriorityClass;
    }

    /// <summary>Gets the unique identifier of the process.</summary>
    public int Id { get; }
    /// <summary>Gets the name of the process.</summary>
    public string ProcessName { get; }
    /// <summary>Gets the time when the process was started.</summary>
    public DateTime StartTime { get; }
    /// <summary>Gets the main module of the process.</summary>
    public ProcessModuleSnapshot? MainModule { get; }
    /// <summary>Gets the collection of modules loaded by the process.</summary>
    public ImmutableArray<ProcessModuleSnapshot> Modules { get; }
    /// <summary>Gets the amount of time the process has spent in user mode.</summary>
    public TimeSpan UserProcessorTime { get; }
    /// <summary>Gets the total processor time used by the process.</summary>
    public TimeSpan TotalProcessorTime { get; }
    /// <summary>Gets the amount of time the process has spent in privileged mode.</summary>
    public TimeSpan PrivilegedProcessorTime { get; }
    /// <summary>Gets the number of handles opened by the process.</summary>
    public int HandleCount { get; }
    /// <summary>Gets the amount of physical memory allocated for the process in bytes.</summary>
    public long WorkingSet64 { get; }
    /// <summary>Gets the amount of virtual memory allocated for the process in bytes.</summary>
    public long VirtualMemorySize64 { get; }
    /// <summary>Gets the amount of private memory allocated for the process in bytes.</summary>
    public long PrivateMemorySize64 { get; }
    /// <summary>Gets the amount of paged memory allocated for the process in bytes.</summary>
    public long PagedMemorySize64 { get; }
    /// <summary>Gets the amount of paged system memory allocated for the process in bytes.</summary>
    public long PagedSystemMemorySize64 { get; }
    /// <summary>Gets the amount of non-paged system memory allocated for the process in bytes.</summary>
    public long NonpagedSystemMemorySize64 { get; }
    /// <summary>Gets the peak working set size of the process in bytes.</summary>
    public long PeakWorkingSet64 { get; }
    /// <summary>Gets the peak virtual memory size of the process in bytes.</summary>
    public long PeakVirtualMemorySize64 { get; }
    /// <summary>Gets the peak paged memory size of the process in bytes.</summary>
    public long PeakPagedMemorySize64 { get; }
    /// <summary>Gets the priority class of the process.</summary>
    public ProcessPriorityClass PriorityClass { get; }
}
