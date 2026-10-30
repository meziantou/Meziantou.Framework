---
title: Reading values
description: Read the limits, the current usage and the statistics of a control group, and handle the absent values.
---

## Values that can be absent

A getter that reads a kernel file returns a [CGroupValue\<T\>](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}) rather than a nullable value, because a value can be
absent for different reasons, which call for different decisions. "The memory controller is not enabled" and "the memory
is unlimited" must not be confused by code that decides whether to apply its own limit.

<!-- ref:../../tests/Meziantou.Framework.Unix.ControlGroups.Tests/CGroup2Tests.cs;region=CGroup2_ReadValues;format=md-fenced-code-block;dedent=true -->
```csharp
var parent = CGroup2.Root.CreateOrGetChild("myapp-values");
parent.SetControllers("memory");
var worker = parent.CreateOrGetChild("worker");

// memory.max holds "max": there is no limit
var memoryMax = worker.GetMemoryMax();
Assert.Equal(CGroupValueState.NotConfigured, memoryMax.State);
Assert.Equal("max", memoryMax.RawValue);

worker.SetMemoryMax(512L * 1024 * 1024);
memoryMax = worker.GetMemoryMax();
Assert.Equal(CGroupValueState.Configured, memoryMax.State);
Assert.Equal(512L * 1024 * 1024, memoryMax.Value);

// The pids controller is not enabled on the parent, so pids.max does not exist
var pidsMax = worker.GetPidsMax();
Assert.Equal(CGroupValueState.Unavailable, pidsMax.State);
Assert.False(pidsMax.TryGetValue(out _));
Assert.Equal(long.MaxValue, pidsMax.GetValueOrDefault(long.MaxValue));

worker.Delete();
parent.Delete();
```
<!-- endref -->

[State](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.State) tells what the file holds:

| [State](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.State) | Meaning |
| --- | --- |
| [Configured](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.Configured) | The file holds a value: `Value` returns it |
| [NotConfigured](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.NotConfigured) | The file holds no limit (`max`) |
| [Unavailable](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.Unavailable) | The file does not exist: the controller is not enabled on the parent, or the kernel does not support the feature |
| [Invalid](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.Invalid) | The file holds content the library does not understand; [RawValue](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.RawValue) keeps it |

Read the value the way that fits your code:

- `switch` on [State](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.State) when the cases call for different actions.
- [TryGetValue](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.TryGetValue) when only a value matters.
- `GetValueOrDefault(fallback)` when a fallback is good enough.
- `Value` throws an `InvalidOperationException` when the state is not [Configured](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.Configured).

[RawValue](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValue{T}.RawValue) gives the content of the file without its surrounding whitespace, or `null` when the file does not exist.

## Limits and usage

| Getter | Returns |
| --- | --- |
| [GetCpuWeight()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpuWeight) | The CPU weight |
| [GetCpuMax()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpuMax) | A [CpuMax](cref:Meziantou.Framework.Unix.ControlGroups.CpuMax): [MaxMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuMax.MaxMicroseconds), `null` when unlimited, and [PeriodMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuMax.PeriodMicroseconds). The state is never [NotConfigured](cref:Meziantou.Framework.Unix.ControlGroups.CGroupValueState.NotConfigured), because the period is meaningful without a limit |
| [GetMemoryMax()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryMax), [GetMemoryHigh()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryHigh), [GetMemoryLow()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryLow), [GetMemoryMin()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryMin), [GetSwapMax()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetSwapMax) | The memory limits, in bytes |
| [GetMemoryCurrent()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryCurrent) | The memory the group uses, in bytes |
| [GetPidsMax()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetPidsMax), [GetPidsCurrent()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetPidsCurrent) | The process limit and the current number of processes |
| [GetCpusetCpus()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetCpus), [GetCpusetCpusEffective()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetCpusEffective), [GetCpusetMems()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetMems), [GetCpusetMemsEffective()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetMemsEffective) | The CPUs and memory nodes |
| [GetCpusetPartition()](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpusetPartition) | The partition type |
| `GetHugeTlbMax(pageSize)`, `GetHugeTlbCurrent(pageSize)`, `GetHugeTlbEventsMax(pageSize)` | The huge page limit, usage and refused allocations |

## Statistics

[GetCpuStat](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetCpuStat) and [GetMemoryStat](cref:Meziantou.Framework.Unix.ControlGroups.CGroup2.GetMemoryStat) read the statistics of the group:

<!-- ref:../../tests/Meziantou.Framework.Unix.ControlGroups.Tests/CGroup2Tests.cs;region=CGroup2_Statistics;format=md-fenced-code-block;dedent=true -->
```csharp
var group = CGroup2.Root.CreateOrGetChild("myapp-statistics");

// TryGetValue when only a value matters
if (group.GetCpuStat().TryGetValue(out var cpuStat))
{
    var cpuTime = TimeSpan.FromMicroseconds(cpuStat.UsageMicroseconds);
    Assert.True(cpuTime >= TimeSpan.Zero);
}

// GetValueOrDefault when a fallback is good enough
var memoryInBytes = group.GetMemoryCurrent().GetValueOrDefault(0);
Assert.True(memoryInBytes >= 0);

group.Delete();
```
<!-- endref -->

- [CpuStat](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat) gives the CPU time the processes used, in microseconds: [UsageMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.UsageMicroseconds), split into
  [UserMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.UserMicroseconds) and [SystemMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.SystemMicroseconds). [NumberOfPeriods](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.NumberOfPeriods), [NumberOfThrottled](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.NumberOfThrottled) and [ThrottledMicroseconds](cref:Meziantou.Framework.Unix.ControlGroups.CpuStat.ThrottledMicroseconds) tell
  how often and how long the CPU limit throttled the group; they are `null` when the kernel does not report them, such as
  when the `cpu` controller is not enabled.
- [MemoryStat](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat) details the memory of the group in bytes, such as [Anon](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat.Anon) (the memory the processes allocated), `File`
  (the file cache), [Kernel](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat.Kernel) and [Sock](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat.Sock), and counts events such as [PageFault](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat.PageFault) and [PageMajorFault](cref:Meziantou.Framework.Unix.ControlGroups.MemoryStat.PageMajorFault).
- A statistic the running kernel does not report is `null`.

To watch a group, read these values periodically: the CPU time grows, so the difference between two readings divided
by the elapsed time gives the CPU usage.

## Numbers

Numbers are read and written with the invariant culture, whatever the culture of the application.
