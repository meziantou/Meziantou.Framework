# Meziantou.Framework.Win32.Jobs

`Meziantou.Framework.Win32.Jobs` is a wrapper for [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects?WT.mc_id=DT-MVP-5003978). A job object allows groups of processes to be managed as a unit. Operations performed on a job object affect all processes associated with the job object. Examples include enforcing limits such as working set size and process priority or terminating all processes associated with a job.

The types are in the `Meziantou.Framework.Win32` namespace, and `Process` is in `System.Diagnostics`.

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

You can also set limits to the Job Object:

<!-- ref:../../samples/Meziantou.Framework.Win32.Jobs.Samples/ReadmeExamples.cs;region=JobObject_SetLimits;format=md-fenced-code-block;dedent=true -->
```csharp
job.SetLimits(new JobObjectLimits()
{
    PerProcessUserTimeLimit = TimeSpan.FromMinutes(1).Ticks,
    PerJobUserTimeLimit = TimeSpan.FromMinutes(10).Ticks,
    MinimumWorkingSetSize = 1 * 1024 * 1024,
    MaximumWorkingSetSize = 100 * 1024 * 1024,
    ProcessMemoryLimit = 200 * 1024 * 1024,
    JobMemoryLimit = 500 * 1024 * 1024,
    ActiveProcessLimit = 10,
});

// Restrict UI features
job.SetUIRestrictions(JobObjectUILimit.ReadClipboard);

// Limit CPU
job.SetCpuRateHardCap(2000); // 20% of the CPU
job.SetCpuRate(1000, 3000); // 10% to 30% of the CPU
job.SetCpuRateWeight(5); // 1 to 9

// Network limits
job.SetNetRateLimits(10000); // 10 KB/s

// Security limits
job.SetSecurityLimits(JobObjectSecurityLimit.NoAdmin);

// IO Rate limits
job.SetIoLimits(new JobIoRateLimits
{
    ControlFlags = JobIoRateFlags.Enable,
    MaxBandwidth = 100,
    MaxIops = 100,
    ReservationIops = 100,
});
```
<!-- endref -->

You can terminate all processes associated to the job. The exit code of the processes is `1` unless you specify one:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_Terminate;format=md-fenced-code-block;dedent=true -->
```csharp
job.Terminate();
job.Terminate(exitCode: 1);
```
<!-- endref -->

You can also get accounting information for processes associated with the job:

<!-- ref:../../tests/Meziantou.Framework.Win32.Jobs.Tests/JobObjectTests.cs;region=JobObject_AccountingInformation;format=md-fenced-code-block;dedent=true -->
```csharp
var basicInfo = job.GetBasicAccountingInformation();
var basicAndIoInfo = job.GetBasicAndIoAccountingInformation();
var memoryInfo = job.GetMemoryAccountingInformation();
```
<!-- endref -->

# Additional resources

- [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects?WT.mc_id=DT-MVP-5003978)
- [Killing all child processes when the parent exits (Job Object)](https://www.meziantou.net/killing-all-child-processes-when-the-parent-exits-job-object.htm)
