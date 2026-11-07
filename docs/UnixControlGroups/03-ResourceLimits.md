---
title: Resource limits
description: Limit the CPU time, CPU set, memory, swap, IO, number of processes and huge pages of a control group.
---

The limits of a control group apply to all its processes together. A limit needs its controller, which you enable on the
**parent** of the control group with [SetControllers](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetControllers): see [How control groups work](01-Introduction.md#how-control-groups-work).

<!-- ref:../../tests/Meziantou.Framework.Unix.ControlGroups.Tests/CGroup2Tests.cs;region=CGroup2_ResourceLimits;format=md-fenced-code-block;dedent=true -->
```csharp
var parent = CGroup2.Root.CreateOrGetChild("myapp-limits");
parent.SetControllers("cpu", "memory", "pids");
var worker = parent.CreateOrGetChild("worker");

// CPU: a relative weight (1 to 10000, 100 by default) and a hard limit of 2 CPUs
worker.SetCpuWeight(50);
worker.SetCpuMax(200_000, 100_000);
Assert.Equal(new CpuMax(200_000, 100_000), worker.GetCpuMax().Value);

// Memory: throttled above 1.5 GB, never more than 2 GB
worker.SetMemoryHigh(1536L * 1024 * 1024);
worker.SetMemoryMax(2048L * 1024 * 1024);

// Processes: at most 100
worker.SetPidsMax(100);

// null removes a limit
worker.SetMemoryHigh(null);
worker.SetPidsMax(null);
worker.RemoveCpuMax();
Assert.Equal(CGroupValueState.NotConfigured, worker.GetPidsMax().State);

worker.Delete();
parent.Delete();
```
<!-- endref -->

Passing `null` to a method that sets one limit, such as [SetMemoryMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetMemoryMax) or [SetPidsMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetPidsMax), removes the limit: the kernel
file then holds `max`.

## CPU

Controller: `cpu`.

| Method | Effect |
| --- | --- |
| `SetCpuWeight(weight)` | The share of CPU time relative to the sibling control groups, from 1 to 10000 (100 by default), when the CPU is busy |
| `SetCpuMax(maxMicroseconds, periodMicroseconds = 100000)` | At most `maxMicroseconds` of CPU time per period: `200_000, 100_000` is 2 CPUs, `50_000, 100_000` half of one. `null` removes the limit and sets the period |
| [RemoveCpuMax()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.RemoveCpuMax) | Removes the limit and keeps the period |

The weight shares the CPU only when processes compete for it; the maximum applies even when the CPU is idle.

## Memory

Controller: `memory`. The values are in bytes.

| Method | Effect |
| --- | --- |
| `SetMemoryMax(bytes)` | The hard limit: above it, the kernel reclaims memory, then kills processes of the group |
| `SetMemoryHigh(bytes)` | The soft limit: above it, the processes are throttled and their memory is reclaimed |
| `SetMemoryLow(bytes)` | Best-effort protection: memory below it is reclaimed only when no other memory can be |
| `SetMemoryMin(bytes)` | Hard protection: memory below it is never reclaimed |
| `SetSwapMax(bytes)` | The swap the group can use; `0` disables swap for it |

## Processes

Controller: `pids`. `SetPidsMax(max)` limits the number of processes and threads of the group: once it is reached,
creating a process fails. It protects the machine from a process that forks without end.

## IO

Controller: `io`. A device is identified by its major and minor numbers, which `ls -l /dev` or `lsblk` show:

<!-- ref:../../samples/Meziantou.Framework.Unix.ControlGroups.Samples/DocumentationExamples.cs;region=CGroup2_IoLimits;format=md-fenced-code-block;dedent=true -->
```csharp
var parent = CGroup2.Root.CreateOrGetChild("myapp");
parent.SetControllers("io");
var worker = parent.CreateOrGetChild("worker");

// A relative weight (1 to 10000, 100 by default), for every device or for one device
worker.SetDefaultIoWeight(200);
worker.SetIoWeight(major: 8, minor: 0, weight: 500); // 8:0 is /dev/sda

// Bandwidth and IOPS limits of a device: 10 MB/s read, 5 MB/s write, 1000 write operations per second
worker.SetIoMax(major: 8, minor: 0, readBytesPerSecond: 10 * 1024 * 1024, writeBytesPerSecond: 5 * 1024 * 1024, writeIopsPerSecond: 1000);

// Remove the limits of the device
worker.RemoveIoMax(major: 8, minor: 0);
```
<!-- endref -->

[SetIoMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetIoMax) sets the limits of a device in bytes per second and in operations per second. Each call changes only the
limits you pass: a limit you leave `null` keeps its current value, which is no limit until you set one. Passing no limit
at all removes the limits of the device, as [RemoveIoMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.RemoveIoMax) does.

## CPU set

Controller: `cpuset`. It chooses the CPUs and the memory nodes the processes can use:

<!-- ref:../../samples/Meziantou.Framework.Unix.ControlGroups.Samples/DocumentationExamples.cs;region=CGroup2_Cpuset;format=md-fenced-code-block;dedent=true -->
```csharp
var parent = CGroup2.Root.CreateOrGetChild("myapp");
parent.SetControllers("cpuset");
var worker = parent.CreateOrGetChild("worker");

// Run on CPUs 2 and 3, and allocate memory on NUMA node 0
worker.SetCpusetCpus(2, 3);
worker.SetCpusetMems(0);

// The same with the kernel list format
worker.SetCpusetCpusRaw("2-3");

// The CPUs the cgroup can actually use, which the parent can restrict further
var effectiveCpus = worker.GetCpusetCpusEffective().GetValueOrDefault([]);
Console.WriteLine($"Effective CPUs: {string.Join(", ", effectiveCpus)}");
```
<!-- endref -->

- [SetCpusetCpus](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetCpus) and [SetCpusetMems](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetMems) take numbers; [SetCpusetCpusRaw](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetCpusRaw) and [SetCpusetMemsRaw](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetMemsRaw) take the kernel format,
  such as `0-3,6,8-10`.
- [GetCpusetCpus](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetCpus) returns what you set, [GetCpusetCpusEffective](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetCpusEffective) what the control group can use, which its parent can
  restrict further. An empty list means the group uses the CPUs of its parent.
- [SetCpusetPartition](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.SetCpusetPartition) sets the partition type: `member`, `root` or `isolated`.

## Huge pages

Controller: `hugetlb`. The methods take the page size as the kernel names it, such as `2MB` or `1GB`:
`SetHugeTlbMax(pageSize, bytes)` limits the use of huge pages of that size, [GetHugeTlbCurrent](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetHugeTlbCurrent) and [GetHugeTlbMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetHugeTlbMax) read
the usage and the limit, and [GetHugeTlbEventsMax](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetHugeTlbEventsMax) counts the allocations the limit refused. The page sizes depend on
the processor and on the kernel configuration: a size the kernel does not provide is reported as unavailable.

## Limits are hierarchical

A control group never gets more than its parent allows: a child of a group limited to 1 GB cannot use 2 GB, whatever its
own limit. Use the hierarchy to share a budget: limit a parent group, then split it among its children with weights.
