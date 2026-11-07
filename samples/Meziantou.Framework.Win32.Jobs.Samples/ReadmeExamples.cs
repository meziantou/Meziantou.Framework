using System.Diagnostics;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.Jobs.Samples;

[SupportedOSPlatform("windows10.0.10240")]
internal static class ReadmeExamples
{
    public static void KillChildProcessesOnExit()
    {
        #region JobObject_KillOnJobClose
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
        #endregion
    }

    public static void SetLimits()
    {
        using var job = new JobObject();

        #region JobObject_SetLimits
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
        #endregion
    }
}
