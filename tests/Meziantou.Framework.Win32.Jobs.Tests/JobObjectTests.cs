using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Jobs.Tests;

[Collection("JobObjectTests")]
public class JobObjectTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void ShouldKillProcessOnTerminate()
    {
        using var job = new JobObject();
        job.SetLimits(new JobObjectLimits()
        {
            Flags = JobObjectLimitFlags.DieOnUnhandledException | JobObjectLimitFlags.KillOnJobClose,
        });

        var psi = new ProcessStartInfo
        {
            FileName = "ping",
            Arguments = "127.0.0.1 -n 100",
            UseShellExecute = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.False(process.WaitForExit(500)); // Ensure process is started

        job.AssignProcess(process);
        job.Terminate();

        process.WaitForExit();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void KillOnJobClose_ShouldKillProcessOnClose()
    {
        using var job = new JobObject();
        job.SetLimits(new JobObjectLimits()
        {
            Flags = JobObjectLimitFlags.DieOnUnhandledException | JobObjectLimitFlags.KillOnJobClose,
        });

        var psi = new ProcessStartInfo
        {
            FileName = "ping",
            Arguments = "127.0.0.1 -n 100",
            UseShellExecute = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.False(process.WaitForExit(500)); // Ensure process is started

        job.AssignProcess(process);
        job.Dispose();

        process.WaitForExit();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CreateAndOpenJobObject()
    {
        var objectName = Guid.NewGuid().ToString("N");
        using var job = new JobObject(objectName);

        using (JobObject.Open(JobObjectAccessRights.AllAccess, inherited: true, objectName))
        {
        }
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void InvalidName_TooLong()
    {
        var objectName = "Local\\" + new string('a', 40000);
        FluentActions.Invoking(() => new JobObject(objectName)).Should().Throw<Win32Exception>();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void InvalidName_InvalidCharacter()
    {
        var objectName = "Local\\a\\b";
        FluentActions.Invoking(() => new JobObject(objectName)).Should().Throw<Win32Exception>();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void TryOpen()
    {
        // The project is multi-targeted, so multiple process can run in parallel
        using var mutex = new Mutex(initiallyOwned: false, "MeziantouFrameworkJobsTests");
        mutex.WaitOne();
        try
        {

            FluentActions.Invoking(() => JobObject.Open(JobObjectAccessRights.Query, false, "JobObjectTests")).Should().Throw<Win32Exception>();
            Assert.False(JobObject.TryOpen(JobObjectAccessRights.Query, false, "JobObjectTests", out JobObject? testObject));
            testObject.Should().BeNull();
            testObject?.Dispose();

            using (new JobObject("JobObjectTests"))
            {
                JobObject job = JobObject.Open(JobObjectAccessRights.Query, false, "JobObjectTests");
                job.Should().NotBeNull();
                job.Dispose();
                Assert.True(JobObject.TryOpen(JobObjectAccessRights.Query, false, "JobObjectTests", out job));
                job.Should().NotBeNull();
                job.Dispose();
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CpuHardRateCap()
    {
        using var job = new JobObject();
        JobObjectCpuHardCap cap;

        cap = job.GetCpuRateHardCap();
        Assert.False(cap.Enabled);

        job.SetCpuRateHardCap(7654);
        cap = job.GetCpuRateHardCap();
        Assert.True(cap.Enabled);
        Assert.Equal(7654, cap.Rate);

        job.DisableCpuRateHardCap();
        cap = job.GetCpuRateHardCap();
        Assert.False(cap.Enabled);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void SetUILimits()
    {
        using var job = new JobObject();
        job.SetUIRestrictions(Natives.JobObjectUILimit.ReadClipboard);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void SetIoRateLimits()
    {
        using var job = new JobObject();
        job.SetIoLimits(new JobIoRateLimits
        {
            ControlFlags = JobIoRateFlags.Enable,
            MaxBandwidth = 100,
            MaxIops = 100,
            ReservationIops = 100,
        });
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void IsAssignedToProcess_NotAssociated()
    {
        using var job = new JobObject();
        Assert.False(job.IsAssignedToProcess(Process.GetCurrentProcess()));
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void IsAssignedToProcess_Associated()
    {
        using var job = new JobObject();
        var process = Process.GetCurrentProcess();
        job.AssignProcess(process);
        Assert.True(job.IsAssignedToProcess(process));
    }
}
