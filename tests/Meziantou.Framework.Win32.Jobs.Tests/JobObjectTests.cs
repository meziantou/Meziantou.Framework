using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Jobs.Tests;

[Collection("JobObjectTests")]
public class JobObjectTests
{
    [RunIfFact(FactOperatingSystem.Windows, Timeout = 5000)]
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

    [RunIfFact(FactOperatingSystem.Windows, Timeout = 5000)]
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
    public void SetUILimits()
    {
        using var job = new JobObject();
        job.SetUIRestrictions(Natives.JobObjectUILimit.ReadClipboard);
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
