# Meziantou.Framework.Unix.ControlGroups

A .NET library for managing Linux Control Groups v2 (cgroups v2).

## Features

- Create, delete, and manage cgroup hierarchies
- Add/remove processes and threads to/from cgroups
- Configure resource limits:
  - **CPU**: Weight-based scheduling, bandwidth limits
  - **Memory**: Hard limits, soft limits, protection levels, swap control
  - **IO**: Weight-based scheduling, bandwidth/IOPS limits per device
  - **PIDs**: Limit number of processes
  - **Cpuset**: CPU and memory node affinity
- Freeze/unfreeze processes in a cgroup
- Kill all processes in a cgroup
- Read statistics (CPU usage, memory usage, etc.)
- Enable/disable controllers per cgroup

## Requirements

- Linux kernel 4.5+ with cgroup v2 enabled
- cgroup v2 filesystem mounted at `/sys/fs/cgroup` (default on most modern distributions)
- Appropriate permissions to manage cgroups

## Usage

### Basic Operations

```csharp
using Meziantou.Framework.Unix.ControlGroups;

// Get the root cgroup
var root = CGroup2.Root;

// Create a new cgroup
var myGroup = root.CreateChild("myapp");

// Add the current process to the cgroup
myGroup.AddProcess(Environment.ProcessId);

// Create nested cgroups
var subGroup = myGroup.CreateChild("worker1");
```

### CPU Control

```csharp
// Enable CPU controller
myGroup.EnableController("cpu");

// Set CPU weight (relative share, 1-10000, default 100)
myGroup.SetCpuWeight(200); // 2x the default share

// Set CPU bandwidth limit (max 50% of 1 CPU)
myGroup.SetCpuMax(50_000, 100_000); // 50ms per 100ms period

// Remove limit
myGroup.RemoveCpuMax();

// Get CPU statistics
var cpuStat = myGroup.GetCpuStat();
if (cpuStat != null)
{
    Console.WriteLine($"CPU Usage: {cpuStat.UsageMicroseconds} μs");
    Console.WriteLine($"User time: {cpuStat.UserMicroseconds} μs");
    Console.WriteLine($"System time: {cpuStat.SystemMicroseconds} μs");
}
```

### Memory Control

```csharp
// Enable memory controller
myGroup.EnableController("memory");

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
Console.WriteLine($"Current memory usage: {currentMemory} bytes");

// Get detailed memory statistics
var memoryStat = myGroup.GetMemoryStat();
if (memoryStat != null)
{
    Console.WriteLine($"Anonymous memory: {memoryStat.Anon} bytes");
    Console.WriteLine($"File cache: {memoryStat.File} bytes");
    Console.WriteLine($"Page faults: {memoryStat.PageFault}");
}
```

### IO Control

```csharp
// Enable IO controller
myGroup.EnableController("io");

// Set default IO weight
myGroup.SetDefaultIoWeight(200);

// Set IO weight for specific device (major:minor = 8:0 for /dev/sda)
myGroup.SetIoWeight(8, 0, 300);

// Set IO bandwidth limits (10 MB/s read, 5 MB/s write)
myGroup.SetIoMax(
    major: 8,
    minor: 0,
    readBytesPerSecond: 10 * 1024 * 1024,
    writeBytesPerSecond: 5 * 1024 * 1024
);

// Set IOPS limits
myGroup.SetIoMax(
    major: 8,
    minor: 0,
    readIopsPerSecond: 1000,
    writeIopsPerSecond: 500
);

// Remove limits
myGroup.RemoveIoMax(8, 0);
```

### PID Control

```csharp
// Enable PIDs controller
myGroup.EnableController("pids");

// Limit to 100 processes
myGroup.SetPidsMax(100);

// Get current number of processes
var currentPids = myGroup.GetPidsCurrent();
Console.WriteLine($"Current processes: {currentPids}");

// Get limit
var maxPids = myGroup.GetPidsMax();
Console.WriteLine($"Max processes: {maxPids}");
```

### CPU Affinity (Cpuset)

```csharp
// Enable cpuset controller
myGroup.EnableController("cpuset");

// Restrict to CPUs 0, 1, 2
myGroup.SetCpusetCpus(0, 1, 2);

// Or use range format
myGroup.SetCpusetCpusRaw("0-2,4,6-8");

// Restrict to memory nodes 0 and 1
myGroup.SetCpusetMems(0, 1);

// Get effective CPUs (actually granted)
var effectiveCpus = myGroup.GetCpusetCpusEffective();
Console.WriteLine($"Effective CPUs: {string.Join(", ", effectiveCpus ?? [])}");

// Set partition type
myGroup.SetCpusetPartition("isolated");
```

### HugeTLB Control

```csharp
// Enable hugetlb controller (if available)
// Note: HugeTLB support depends on kernel configuration

// Set HugeTLB limit for 2MB pages
myGroup.SetHugeTlbMax("2MB", 100 * 1024 * 1024); // 100 MB

// Get current HugeTLB usage
var current = myGroup.GetHugeTlbCurrent("2MB");
Console.WriteLine($"Current 2MB HugeTLB usage: {current} bytes");

// Get limit hit count
var limitHits = myGroup.GetHugeTlbEventsMax("2MB");
Console.WriteLine($"HugeTLB limit hits: {limitHits}");
```

### Process Management

```csharp
// Add a process
using var process = Process.Start("myapp");
myGroup.AddProcess(process);

// Or by PID
myGroup.AddProcess(1234);

// Add a thread by TID
myGroup.AddThread(5678);

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
```

### Freezer

```csharp
// Freeze all processes (SIGSTOP)
myGroup.Freeze();

// Check if frozen
if (myGroup.IsFrozen())
{
    Console.WriteLine("Cgroup is frozen");
}

// Unfreeze
myGroup.Unfreeze();
```

### Kill All Processes

```csharp
// Kill all processes in the cgroup (SIGKILL)
myGroup.Kill();
```

### Controller Management

```csharp
// Get available controllers
var available = myGroup.GetAvailableControllers();
Console.WriteLine($"Available: {string.Join(", ", available)}");

// Get enabled controllers
var enabled = myGroup.GetEnabledControllers();
Console.WriteLine($"Enabled: {string.Join(", ", enabled)}");

// Enable multiple controllers at once
myGroup.EnableControllers("cpu", "memory", "io");

// Disable a controller
myGroup.DisableController("io");
```

### Cleanup

```csharp
// Delete a cgroup (must be empty - no processes, no child cgroups)
subGroup.Delete();
myGroup.Delete();
```

## Important Notes

1. **Permissions**: Managing cgroups typically requires root privileges or appropriate capabilities.

2. **No Internal Process Constraint**: Non-root cgroups can only enable controllers if they don't contain any processes. Move processes to leaf cgroups first.

3. **Hierarchical**: Resource limits are hierarchical. A child cgroup can't use more resources than its parent allows.

4. **Culture Invariant**: All number parsing and formatting use `CultureInfo.InvariantCulture` for consistency.

5. **cgroup v2 Only**: This library only supports cgroup v2. For systems still using cgroup v1, consider upgrading or use v1-specific tools.

6. **Error Handling**: File operations may throw `IOException`, `UnauthorizedAccessException`, or `DirectoryNotFoundException`. Handle these appropriately.

## Example: Complete Application Resource Limiting

```csharp
using Meziantou.Framework.Unix.ControlGroups;
using System.Diagnostics;

// Create a cgroup for the application
var appGroup = CGroup2.Root.CreateOrGetChild("myapp");

// Enable controllers
appGroup.EnableControllers("cpu", "memory", "io", "pids");

// Configure limits
appGroup.SetCpuWeight(100);       // Normal priority
appGroup.SetCpuMax(200_000, 100_000);    // Max 2 CPUs
appGroup.SetMemoryMax(2L * 1024 * 1024 * 1024); // 2 GB
appGroup.SetMemoryHigh(1536L * 1024 * 1024 * 1024); // Start throttling at 1.5 GB
appGroup.SetPidsMax(200);       // Max 200 processes

// Start your application
var process = Process.Start("myapp");
appGroup.AddProcess(process);

// Monitor
while (!process.HasExited)
{
  var cpuStat = appGroup.GetCpuStat();
    var memCurrent = appGroup.GetMemoryCurrent();
    var pidsCurrent = appGroup.GetPidsCurrent();
    
    Console.WriteLine($"CPU: {cpuStat?.UsageMicroseconds} μs");
    Console.WriteLine($"Memory: {memCurrent / (1024.0 * 1024):F2} MB");
    Console.WriteLine($"Processes: {pidsCurrent}");
    
    await Task.Delay(1000);
}

// Cleanup
appGroup.Delete();
```

## References

- [Linux Kernel cgroup v2 Documentation](https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html)
- [Red Hat: Introduction to Control Groups (cgroups) v2](https://www.redhat.com/sysadmin/cgroups-part-one)
