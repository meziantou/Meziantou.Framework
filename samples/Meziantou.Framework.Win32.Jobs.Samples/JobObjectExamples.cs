using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.Jobs.Samples;

// Examples of the documentation (docs/71-Win32). They restrict the processes of a job, so they are compiled in CI to
// keep the documentation in sync with the API, but not run.
[SupportedOSPlatform("windows10.0.10240")]
public static class JobObjectExamples
{
    public static void OtherLimits()
    {
        #region JobObject_OtherLimits
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
        #endregion
    }
}
