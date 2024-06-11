using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Jobs.Tests;

[Collection("JobObjectTests")]
public class JobObjectTests
{
    [RunIfFact(FactOperatingSystem.Windows)]
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
        process.WaitForExit(500).Should().BeFalse(); // Ensure process is started

        job.AssignProcess(process);
        job.Terminate();

        process.WaitForExit();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
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
        process.WaitForExit(500).Should().BeFalse(); // Ensure process is started

        job.AssignProcess(process);
        job.Dispose();

        process.WaitForExit();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void CreateAndOpenJobObject()
    {
        var objectName = Guid.NewGuid().ToString("N");
        using var job = new JobObject(objectName);

        using (JobObject.Open(JobObjectAccessRights.AllAccess, inherited: true, objectName))
        {
        }
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void InvalidName_TooLong()
    {
        var objectName = "Local\\" + new string('a', 40000);
        FluentActions.Invoking(() => new JobObject(objectName)).Should().Throw<Win32Exception>();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void InvalidName_InvalidCharacter()
    {
        var objectName = "Local\\a\\b";
        FluentActions.Invoking(() => new JobObject(objectName)).Should().Throw<Win32Exception>();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void TryOpen()
    {
        FluentActions.Invoking(() => JobObject.Open(JobObjectAccessRights.Query, false, "JobObjectTests")).Should().Throw<Win32Exception>();

        JobObject.TryOpen(JobObjectAccessRights.Query, false, "JobObjectTests", out JobObject? testObject).Should().BeFalse();
        testObject.Should().BeNull();
        testObject?.Dispose();

        using (new JobObject("JobObjectTests"))
        {
            JobObject job = JobObject.Open(JobObjectAccessRights.Query, false, "JobObjectTests");
            job.Should().NotBeNull();
            job.Dispose();

            JobObject.TryOpen(JobObjectAccessRights.Query, false, "JobObjectTests", out job).Should().BeTrue();
            job.Should().NotBeNull();
            job.Dispose();
        }
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void CpuHardRateCap()
    {
        using var job = new JobObject();
        JobObjectCpuHardCap cap;

        cap = job.GetCpuRateHardCap();
        cap.Enabled.Should().BeFalse();

        job.SetCpuRateHardCap(7654);
        cap = job.GetCpuRateHardCap();
        cap.Enabled.Should().BeTrue();
        cap.Rate.Should().Be(7654);

        job.DisableCpuRateHardCap();
        cap = job.GetCpuRateHardCap();
        cap.Enabled.Should().BeFalse();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void SetUILimits()
    {
        using var job = new JobObject();
        job.SetUIRestrictions(Natives.JobObjectUILimit.ReadClipboard);
    }
    
    [RunIfFact(FactOperatingSystem.Windows)]
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

    [RunIfFact(FactOperatingSystem.Windows)]
    public void IsAssignedToProcess_NotAssociated()
    {
        using var job = new JobObject();
        job.IsAssignedToProcess(Process.GetCurrentProcess()).Should().BeFalse();
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void IsAssignedToProcess_Associated()
    {
        using var job = new JobObject();
        var process = Process.GetCurrentProcess();
        job.AssignProcess(process);

        job.IsAssignedToProcess(process).Should().BeTrue();
    }
}
