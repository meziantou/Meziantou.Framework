---
title: Job objects
description: Group processes in a Windows job object to terminate them together, limit their memory, CPU, IO and network, and read their usage.
---

A job object groups processes so Windows manages them as a unit: it applies limits to all of them, terminates them
together, and accounts for their resources. The processes that a process of the job starts belong to the job too.
`JobObject` from the `Meziantou.Framework.Win32.Jobs` package creates and configures job objects.

```bash
dotnet add package Meziantou.Framework.Win32.Jobs
```

## Terminate the child processes when the application exits

The most common use: start child processes, and make sure they never outlive the application, even when it crashes.
Create a job with the `KillOnJobClose` flag, and assign the processes to it:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_KillOnJobClose;format=md-fenced-code-block;dedent=true -->
```csharp
using var job = new JobObject();
job.SetLimits(new JobObjectLimits
{
    Flags = JobObjectLimitFlags.KillOnJobClose,
});

// Start a child process and assign it to the job
using var childProcess = Process.Start(new ProcessStartInfo("ping", "127.0.0.1 -n 100") { CreateNoWindow = true })!;
job.AssignProcess(childProcess);

// Disposing the job terminates the child process
job.Dispose();
childProcess.WaitForExit();
```
<!-- endref -->

Windows terminates the processes of the job when the last handle to the job is closed: when the job is disposed, or
when the process that created it exits for any reason, as Windows then closes its handles. Keep the `JobObject` alive
for as long as the child processes must run.

A process started before its assignment can start its own children in the meantime, which do not belong to the job. To
cover every descendant, assign the current process to the job before starting the children, as they then belong to the
job from the start:

<!-- ref:../../samples/Meziantou.Framework.Win32.Jobs.Samples/ReadmeExamples.cs;region=JobObject_KillOnJobClose;format=md-fenced-code-block;dedent=true -->
```csharp
// Create the Job object and assign it to the current process
using var job = new JobObject();
job.SetLimits(new JobObjectLimits()
{
    Flags = JobObjectLimitFlags.DieOnUnhandledException |
            JobObjectLimitFlags.KillOnJobClose,
});

using var currentProcess = Process.GetCurrentProcess();
job.AssignProcess(currentProcess);

// Start a child process. This process will be terminated if the current process exits
// as the job has the flag KillOnJobClose.
using var process = Process.Start("child.exe");
process?.WaitForExit();
```
<!-- endref -->

`DieOnUnhandledException` stops Windows from showing the error dialog when a process of the job crashes: the process
terminates instead.

## Limit the processes

`SetLimits` applies the limits of a `JobObjectLimits` to the job. A property left `null`, the default, sets no limit.
Each call replaces all the limits of the previous call, so set them in a single call:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_CombineLimits;format=md-fenced-code-block;dedent=true -->
```csharp
using var job = new JobObject();

// SetLimits replaces every limit set by a previous call: set them all at once
job.SetLimits(new JobObjectLimits
{
    Flags = JobObjectLimitFlags.KillOnJobClose | JobObjectLimitFlags.DieOnUnhandledException,
    ActiveProcessLimit = 4,
    ProcessMemoryLimit = 512 * 1024 * 1024, // 512 MB per process
    PerProcessUserTimeLimit = TimeSpan.FromMinutes(5).Ticks,
});
```
<!-- endref -->

| Property | Limit |
| --- | --- |
| `Flags` | Options such as `KillOnJobClose`, `DieOnUnhandledException`, `BreakawayOk` and `SilentBreakawayOk` (child processes can leave the job) |
| `ActiveProcessLimit` | The number of processes that can run in the job at the same time. `0` is a limit of zero processes: assigning a process fails |
| `ProcessMemoryLimit`, `JobMemoryLimit` | The virtual memory, in bytes, that each process or the whole job can commit |
| `MinimumWorkingSetSize`, `MaximumWorkingSetSize` | The working set of each process, in bytes. Windows applies them only when both are set |
| `PerProcessUserTimeLimit`, `PerJobUserTimeLimit` | The user-mode CPU time, in 100-nanosecond ticks, as `TimeSpan.Ticks` returns |
| `Affinity` | The processors the processes can run on |
| `PriorityClass`, `SchedulingClass` | The priority class and the scheduling class of the processes |

## Limit the CPU

The CPU rate is a number of cycles per 10,000: 2,000 is 20% of the CPU.

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_CpuRate;format=md-fenced-code-block;dedent=true -->
```csharp
using var job = new JobObject();

// The rate is a number of cycles per 10,000: 2,000 is 20% of the CPU
job.SetCpuRateHardCap(2_000);

var cpuRate = job.GetCpuRateHardCap();
Assert.Equal(JobObjectCpuRateControlMode.HardCap, cpuRate.Mode);
Assert.Equal(2_000, cpuRate.Rate);

job.DisableCpuRateHardCap();
Assert.Equal(JobObjectCpuRateControlMode.Disabled, job.GetCpuRateHardCap().Mode);
```
<!-- endref -->

| Method | Policy |
| --- | --- |
| `SetCpuRateHardCap(rate)` | The job never uses more than the rate: once it reaches it, its threads wait for the next scheduling interval |
| `SetCpuRate(minRate, maxRate)` | The job is guaranteed `minRate` and limited to `maxRate` |
| `SetCpuRateWeight(weight)` | The job gets a share of the CPU relative to the other jobs, from 1 to 9. The default weight is 5 |
| `DisableCpuRateHardCap()` | Removes the CPU limit |

The rates are between 1 and 10,000; other values throw an `ArgumentOutOfRangeException`. `GetCpuRateHardCap()`
returns the active policy in `Mode`. Only the properties of that policy are meaningful: `Rate` for `HardCap`, `Weight`
for `Weight`, `MinRate` and `MaxRate` for `MinMaxRate`.

## Limit the IO, the network and the UI

The other limits of the job each have their own method:

<!-- ref:../../samples/Meziantou.Framework.Win32.Jobs.Samples/JobObjectExamples.cs;region=JobObject_OtherLimits;format=md-fenced-code-block;dedent=true -->
```csharp
using var job = new JobObject();

// Disk IO: at most 100 operations per second
job.SetIoLimits(new JobIoRateLimits
{
    ControlFlags = JobIoRateFlags.Enable,
    MaxIops = 100,
});

// Outgoing network traffic: at most 1 MB per second
job.SetNetRateLimits(1_000_000);

// Prevent the processes from reading and writing the clipboard
job.SetUIRestrictions(JobObjectUILimit.ReadClipboard | JobObjectUILimit.WriteClipboard);
```
<!-- endref -->

- `SetIoLimits` limits the disk IO of the job, in operations per second (`MaxIops`) or bytes per second
  (`MaxBandwidth`), and can reserve a minimum (`ReservationIops`). Set `ControlFlags` to `JobIoRateFlags.Enable`.
  `VolumeName` restricts the policy to a volume. It requires Windows 10.
- `SetNetRateLimits` limits the outgoing network traffic of the job, in bytes.
- `SetUIRestrictions` prevents the processes from using the user-interface handles, such as the windows, of the
  processes outside the job (`Handles`), reading or writing the clipboard, using the global atoms, changing the display
  settings or the system parameters, switching desktops, or logging off the user. Combine the `JobObjectUILimit` flags.
- `SetSecurityLimits` is rejected by current versions of Windows, which no longer support security limits on a job: it
  throws a `Win32Exception` ("The request is not supported").

## Terminate the processes

`Terminate` terminates all the processes of the job, and of the jobs nested in it. Their exit code is `1` unless you
specify one:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_Terminate;format=md-fenced-code-block;dedent=true -->
```csharp
job.Terminate();
job.Terminate(exitCode: 1);
```
<!-- endref -->

## Read the usage of the job

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_AccountingInformation;format=md-fenced-code-block;dedent=true -->
```csharp
var basicInfo = job.GetBasicAccountingInformation();
var basicAndIoInfo = job.GetBasicAndIoAccountingInformation();
var memoryInfo = job.GetMemoryAccountingInformation();
```
<!-- endref -->

- `GetBasicAccountingInformation` returns the CPU time (`TotalUserTime`, `TotalKernelTime`), the page faults and the
  number of processes (`ActiveProcesses`, `TotalProcesses`, `TotalTerminatedProcesses`).
- `GetBasicAndIoAccountingInformation` adds the IO counters: the number of read, write and other operations, and the
  bytes they transferred.
- `GetMemoryAccountingInformation` returns the peak memory used by the job and by one of its processes
  (`PeakJobMemoryUsed`, `PeakProcessMemoryUsed`).

Windows can add helper processes, such as the console host of a console application, to the job: the counts include
them.

## Share a job between processes

A job object can have a name. Another process opens it by its name with `JobObject.Open`, or `JobObject.TryOpen`, which
returns `false` when no job object has this name:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_NamedJob;format=md-fenced-code-block;dedent=true -->
```csharp
var name = "MyApplication-" + Guid.NewGuid().ToString("N");

// CreatedNew is false when a job object with this name already exists: the instance attaches to it
using var job = new JobObject(name);
Assert.True(job.CreatedNew);

// Another process can open the job object by its name
if (JobObject.TryOpen(JobObjectAccessRights.Query | JobObjectAccessRights.AssignProcess, inherited: false, name, out var existingJob))
{
    using (existingJob)
    {
        using var currentProcess = Process.GetCurrentProcess();
        Assert.False(existingJob.IsAssignedToProcess(currentProcess));
    }
}
```
<!-- endref -->

- When a job object with the name already exists, the constructor attaches to it instead of creating a new one:
  `CreatedNew` is then `false`, and the instance shares the limits, the processes and the lifetime of the existing job.
  Use a unique name, or check `CreatedNew`, when the application needs a job of its own.
- `JobObjectAccessRights` sets what the opened instance can do: `Query`, `AssignProcess`, `Terminate`, `SetAttributes`
  and so on. A call that needs a right the handle does not have throws a `Win32Exception`.
- `IsAssignedToProcess` tells whether a process belongs to the job.

The methods throw a `Win32Exception` when Windows rejects the operation.
