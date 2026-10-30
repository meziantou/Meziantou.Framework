---
title: Resource limits
description: Limit the CPU, the memory and the number of processes of a command, with Windows Job Objects or Linux control groups.
---

## Limit CPU, memory and processes

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_ResourceLimits;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-app")
    .WithLimits(new ProcessLimits
    {
        CpuPercentage = 50,            // 50% max CPU
        MemoryLimitInBytes = 512L * 1024 * 1024, // 512 MB
        ProcessCountLimit = 20,
    })
    .ExecuteAsync();
```
<!-- endref -->

| `ProcessLimits` property | Limit |
| --- | --- |
| `CpuPercentage` | The maximum CPU usage, between 1 and 100 |
| `MemoryLimitInBytes` | The maximum memory usage, in bytes |
| `ProcessCountLimit` | The maximum number of processes in the group: the process and its descendants |

`WithLimits(ProcessLimits)` replaces the limits, and `WithLimits(Action<ProcessLimits>)` changes the current ones.

The limits are applied with a Job Object on Windows and with a cgroup v2 control group on Linux. Other operating
systems, macOS included, throw a `PlatformNotSupportedException` when a limit is set. An out-of-range value throws an
`ArgumentOutOfRangeException` when the process starts.

The limits are applied once the process is started. On Windows with .NET 11, the process is started suspended and
resumed once the limits are applied, so it never runs without them.

## Windows Job Objects

`WithWindowsJobObject` gives access to the `JobObject` (in the [Meziantou.Framework.Win32](cref:Meziantou.Framework.Win32.Jobs!N:Meziantou.Framework.Win32) namespace) for the settings that
`ProcessLimits` does not cover:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_WindowsJobObject;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-app")
    .WithLimits(limits => limits.MemoryLimitInBytes = 512L * 1024 * 1024)
    .WithWindowsJobObject(job =>
    {
        job.SetLimits(new JobObjectLimits
        {
            Flags = JobObjectLimitFlags.KillOnJobClose,
        });
    })
    .ExecuteAsync();
```
<!-- endref -->

The callback runs on Windows only. Using `WithWindowsJobObject` on another operating system throws a
`PlatformNotSupportedException` when the process starts.

## Linux control groups

`WithLinuxControlGroup` gives access to the `CGroup2` (in the [Meziantou.Framework.Unix.ControlGroups](cref:Meziantou.Framework.Unix.ControlGroups) namespace):

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_LinuxControlGroup;format=md-fenced-code-block;dedent=true -->
```csharp
var result = await ProcessWrapper.Create("my-app")
    .WithLinuxControlGroup(cgroup =>
    {
        cgroup.SetMemoryHigh(256L * 1024 * 1024);
    })
    .ExecuteAsync();
```
<!-- endref -->

The callback runs on Linux only. Using `WithLinuxControlGroup` on another operating system throws a
`PlatformNotSupportedException` when the process starts.
