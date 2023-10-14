using System.Collections.Immutable;
using System.Diagnostics;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public class ProcessSnapshot
{
    internal ProcessSnapshot(Process process)
    {
        Id = process.Id;
        ProcessName = process.ProcessName;
        StartTime = process.StartTime;

        MainModule = process.MainModule == null ? null : new ProcessModuleSnapshot(process.MainModule);
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

    public int Id { get; }
    public string ProcessName { get; }
    public DateTime StartTime { get; }
    public ProcessModuleSnapshot? MainModule { get; }
    public ImmutableArray<ProcessModuleSnapshot> Modules { get; }
    public TimeSpan UserProcessorTime { get; }
    public TimeSpan TotalProcessorTime { get; }
    public TimeSpan PrivilegedProcessorTime { get; }
    public int HandleCount { get; }
    public long WorkingSet64 { get; }
    public long VirtualMemorySize64 { get; }
    public long PrivateMemorySize64 { get; }
    public long PagedMemorySize64 { get; }
    public long PagedSystemMemorySize64 { get; }
    public long NonpagedSystemMemorySize64 { get; }
    public long PeakWorkingSet64 { get; }
    public long PeakVirtualMemorySize64 { get; }
    public long PeakPagedMemorySize64 { get; }
    public ProcessPriorityClass PriorityClass { get; }
}
