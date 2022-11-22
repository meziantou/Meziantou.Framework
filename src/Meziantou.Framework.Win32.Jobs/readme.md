# Meziantou.Framework.Win32.Jobs

`Meziantou.Framework.Win32.Jobs` is a wrapper for [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects?WT.mc_id=DT-MVP-5003978). A job object allows groups of processes to be managed as a unit. Operations performed on a job object affect all processes associated with the job object. Examples include enforcing limits such as working set size and process priority or terminating all processes associated with a job.

```c#
// Create the Job object and assign it to the current process
using var job = new JobObject();
job.SetLimits(new JobObjectLimits()
{
     Flags = JobObjectLimitFlags.DieOnUnhandledException |
             JobObjectLimitFlags.KillOnJobClose,
});

job.AssignProcess(Process.GetCurrentProcess());

// Start a child process. This process will be terminated if the current process exits
// as the job has the flag KillOnJobClose.
var process = Process.Start("child");
process.WaitForExit();
```

You can also set limits to the Job Object:

````c#
job.SetLimits(new JobObjectLimits()
{
     PerProcessUserTimeLimit = ...,
     PerJobUserTimeLimit = ...,
     MinimumWorkingSetSize = ...,
     MaximumWorkingSetSize = ...,
     ProcessMemoryLimit = ...,
     JobMemoryLimit = ...,
     ActiveProcessLimit = ...,
});

// Restrict UI features
job.SetUIRestrictions(Natives.JobObjectUILimit.ReadClipboard);

// Limit CPU
job.SetCpuRateHardCap(2000); // 20% of the CPU
job.SetCpuRate(1000, 3000); // 10% to 30% of the CPU
job.SetCpuRateWeight(5); // 1 to 9

// Network limits
job.SetNetRateLimits(10000); // 10Kb/s

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
````

You can terminate all processes associated to the job:

````
job.Terminate();
job.Terminate(exitCode: 1);
````

# Additional resources

- [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects?WT.mc_id=DT-MVP-5003978)
- [Killing all child processes when the parent exits (Job Object)](https://www.meziantou.net/killing-all-child-processes-when-the-parent-exits-job-object.htm)
