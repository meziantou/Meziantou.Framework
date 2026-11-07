using System.Diagnostics;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups.Samples;

// The examples of src/Meziantou.Framework.Unix.ControlGroups/readme.md. They change the cgroups of the machine, which
// needs Linux and root, so they are compiled but not run. The tests of ControlGroups cover the same calls in a cgroup
// of their own.
[SupportedOSPlatform("linux")]
public static class ReadmeExamples
{
    // The sections of the "Usage" part of the readme, which continue each other
    public static void Usage()
    {
        #region CGroup2_BasicOperations
        // Get the root cgroup
        var root = CGroup2.Root;

        // Create a new cgroup
        var myGroup = root.CreateOrGetChild("myapp");

        // Add the current process to the cgroup
        myGroup.AssociateProcess(Environment.ProcessId);

        // Create nested cgroups
        var subGroup = myGroup.CreateOrGetChild("worker1");
        #endregion

        #region CGroup2_CpuControl
        // Enable the CPU controller for the children of the root cgroup, which is where myGroup lives.
        // Controllers are always enabled on the PARENT, never on the cgroup being limited.
        root.SetControllers("cpu");

        // Set CPU weight (relative share, 1-10000, default 100)
        myGroup.SetCpuWeight(200); // 2x the default share

        // Set CPU bandwidth limit (max 50% of 1 CPU)
        myGroup.SetCpuMax(50_000, 100_000); // 50ms per 100ms period

        // Remove limit
        myGroup.RemoveCpuMax();

        // Get CPU statistics
        if (myGroup.GetCpuStat().TryGetValue(out var cpuStat))
        {
            Console.WriteLine($"CPU Usage: {cpuStat.UsageMicroseconds} μs");
            Console.WriteLine($"User time: {cpuStat.UserMicroseconds} μs");
            Console.WriteLine($"System time: {cpuStat.SystemMicroseconds} μs");
        }
        #endregion

        #region CGroup2_MemoryControl
        // Enable the memory controller on the parent (see the note in "CPU Control")
        root.SetControllers("memory");

        // Set hard memory limit (1 GB)
        myGroup.SetMemoryMax(1024L * 1024 * 1024);

        // Set soft limit (throttling starts at 512 MB)
        myGroup.SetMemoryHigh(512L * 1024 * 1024);

        // Set memory protection (best-effort, prevents reclaim below 256 MB)
        myGroup.SetMemoryLow(256L * 1024 * 1024);

        // Set swap limit
        myGroup.SetSwapMax(512L * 1024 * 1024);

        // Get current memory usage
        var currentMemory = myGroup.GetMemoryCurrent();
        Console.WriteLine($"Current memory usage: {currentMemory.Value} bytes");

        // Get the hard limit. null means "unlimited"; "not enabled" and "unlimited" are different answers
        var max = myGroup.GetMemoryMax();
        Console.WriteLine(max.State switch
        {
            CGroupValueState.Configured => $"Limited to {max.Value} bytes",
            CGroupValueState.NotConfigured => "No memory limit",
            CGroupValueState.Unavailable => "The memory controller is not enabled on the parent cgroup",
            _ => $"Unexpected memory.max content: '{max.RawValue}'",
        });

        // Get detailed memory statistics
        if (myGroup.GetMemoryStat().TryGetValue(out var memoryStat))
        {
            Console.WriteLine($"Anonymous memory: {memoryStat.Anon} bytes");
            Console.WriteLine($"File cache: {memoryStat.File} bytes");
            Console.WriteLine($"Page faults: {memoryStat.PageFault}");
        }
        #endregion

        #region CGroup2_IoControl
        // Enable the IO controller on the parent
        root.SetControllers("io");

        // Set default IO weight
        myGroup.SetDefaultIoWeight(200);

        // Set IO weight for specific device (major:minor = 8:0 for /dev/sda)
        myGroup.SetIoWeight(8, 0, 300);

        // Set IO bandwidth limits (10 MB/s read, 5 MB/s write)
        myGroup.SetIoMax(
            major: 8,
            minor: 0,
            readBytesPerSecond: 10 * 1024 * 1024,
            writeBytesPerSecond: 5 * 1024 * 1024);

        // Set IOPS limits
        myGroup.SetIoMax(
            major: 8,
            minor: 0,
            readIopsPerSecond: 1000,
            writeIopsPerSecond: 500);

        // Remove limits
        myGroup.RemoveIoMax(8, 0);
        #endregion

        #region CGroup2_PidControl
        // Enable the PIDs controller on the parent
        root.SetControllers("pids");

        // Limit to 100 processes
        myGroup.SetPidsMax(100);

        // Get current number of processes
        var currentPids = myGroup.GetPidsCurrent();
        Console.WriteLine($"Current processes: {currentPids.Value}");

        // Get limit (GetValueOrDefault avoids handling every state when a fallback is good enough)
        var maxPids = myGroup.GetPidsMax();
        Console.WriteLine($"Max processes: {maxPids.GetValueOrDefault(long.MaxValue)}");
        #endregion

        #region CGroup2_Cpuset
        // Enable the cpuset controller on the parent
        root.SetControllers("cpuset");

        // Restrict to CPUs 0, 1, 2
        myGroup.SetCpusetCpus(0, 1, 2);

        // Or use range format
        myGroup.SetCpusetCpusRaw("0-2,4,6-8");

        // Restrict to memory nodes 0 and 1
        myGroup.SetCpusetMems(0, 1);

        // Get effective CPUs (actually granted)
        var effectiveCpus = myGroup.GetCpusetCpusEffective();
        Console.WriteLine($"Effective CPUs: {string.Join(", ", effectiveCpus.GetValueOrDefault([]))}");

        // Set partition type
        myGroup.SetCpusetPartition("isolated");
        #endregion

        #region CGroup2_HugeTlb
        // Enable hugetlb controller (if available)
        // Note: HugeTLB support depends on kernel configuration

        // Set HugeTLB limit for 2MB pages
        myGroup.SetHugeTlbMax("2MB", 100 * 1024 * 1024); // 100 MB

        // Get current HugeTLB usage. Unavailable when the kernel does not provide this page size
        var current = myGroup.GetHugeTlbCurrent("2MB");
        if (current.IsConfigured)
        {
            Console.WriteLine($"Current 2MB HugeTLB usage: {current.Value} bytes");
        }

        // Get limit hit count
        var limitHits = myGroup.GetHugeTlbEventsMax("2MB");
        Console.WriteLine($"HugeTLB limit hits: {limitHits.GetValueOrDefault(0)}");
        #endregion

        #region CGroup2_ProcessManagement
        // Associate a process
        using var process = Process.Start("myapp");
        myGroup.AssociateProcess(process);

        // Or by PID
        myGroup.AssociateProcess(1234);

        // Add a thread by TID
        myGroup.AssociateThread(5678);

        // Get all processes in the cgroup
        foreach (var pid in myGroup.GetProcesses())
        {
            Console.WriteLine($"Process: {pid}");
        }

        // Get all threads
        foreach (var tid in myGroup.GetThreads())
        {
            Console.WriteLine($"Thread: {tid}");
        }
        #endregion

        #region CGroup2_Freezer
        // Freeze all processes (SIGSTOP)
        myGroup.Freeze();

        // Check if frozen
        if (myGroup.IsFrozen())
        {
            Console.WriteLine("Cgroup is frozen");
        }

        // Unfreeze
        myGroup.Unfreeze();
        #endregion

        #region CGroup2_Kill
        // Kill all processes in the cgroup (SIGKILL)
        myGroup.Kill();
        #endregion

        #region CGroup2_ControllerManagement
        // Get available controllers
        var available = myGroup.GetAvailableControllers();
        Console.WriteLine($"Available: {string.Join(", ", available)}");

        // Get enabled controllers
        var enabled = myGroup.GetEnabledControllers();
        Console.WriteLine($"Enabled: {string.Join(", ", enabled)}");

        // Enable multiple controllers at once for myGroup's children
        myGroup.SetControllers("cpu", "memory", "io");

        // Disable a controller
        // Prefixing a controller name with '-' disables that controller. For example, "-io" disables the IO controller.
        myGroup.SetControllers("-io");
        #endregion

        #region CGroup2_Cleanup
        // Delete a cgroup (must be empty - no processes, no child cgroups)
        subGroup.Delete();
        myGroup.Delete();
        #endregion
    }

    public static async Task CompleteApplicationResourceLimiting()
    {
        #region CGroup2_CompleteExample
        // Create a cgroup for the application
        var appGroup = CGroup2.Root.CreateOrGetChild("myapp");

        // Enable controllers on the parent so they apply to appGroup
        CGroup2.Root.SetControllers("cpu", "memory", "io", "pids");

        // Configure limits
        appGroup.SetCpuWeight(100);       // Normal priority
        appGroup.SetCpuMax(200_000, 100_000);    // Max 2 CPUs
        appGroup.SetMemoryMax(2L * 1024 * 1024 * 1024); // 2 GB
        appGroup.SetMemoryHigh(1536L * 1024 * 1024); // Start throttling at 1.5 GB
        appGroup.SetPidsMax(200);       // Max 200 processes

        // Start your application
        var process = Process.Start("myapp");
        appGroup.AssociateProcess(process);

        // Monitor
        while (!process.HasExited)
        {
            var cpuStat = appGroup.GetCpuStat();
            var memCurrent = appGroup.GetMemoryCurrent();
            var pidsCurrent = appGroup.GetPidsCurrent();

            Console.WriteLine($"CPU: {cpuStat.Value.UsageMicroseconds} μs");
            Console.WriteLine($"Memory: {memCurrent.Value / (1024.0 * 1024):F2} MB");
            Console.WriteLine($"Processes: {pidsCurrent.Value}");

            await Task.Delay(1000);
        }

        // Cleanup
        appGroup.Delete();
        #endregion
    }
}
