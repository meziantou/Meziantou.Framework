using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Jobs.Tests
{
    [TestClass]
    public class JobObjectTests
    {
        [TestMethod]
        [Timeout(5000)]
        public void ShouldKillProcessOnTerminate()
        {
            using (var job = new JobObject())
            {
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

                using (var process = Process.Start(psi))
                {
                    Assert.IsFalse(process.WaitForExit(500)); // Ensure process is started

                    job.AssignProcess(process);
                    job.Terminate();

                    process.WaitForExit();
                }
            }
        }

        [TestMethod]
        [Timeout(5000)]
        public void KillOnJobClose_ShouldKillProcessOnClose()
        {
            using (var job = new JobObject())
            {
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

                using (var process = Process.Start(psi))
                {
                    Assert.IsFalse(process.WaitForExit(500)); // Ensure process is started

                    job.AssignProcess(process);
                    job.Close();

                    process.WaitForExit();
                }
            }
        }

        [TestMethod]
        public void CreateAndOpenJobObject()
        {
            var objectName = Guid.NewGuid().ToString("N");
            using (var job = new JobObject(objectName))
            {
                Assert.IsFalse(job.IsInvalid);

                using (var openedJob = JobObject.Open(JobObjectAccessRights.AllAccess, inherited: true, objectName))
                {
                    Assert.IsFalse(openedJob.IsInvalid);
                }
            }
        }

        [TestMethod]
        public void SetUILimits()
        {
            using (var job = new JobObject())
            {
                job.SetUIRestrictions(Natives.JobObjectUILimit.ReadClipboard);
            }
        }

        [TestMethod]
        public void IsAssignedToProcess_NotAssociated()
        {
            using (var job = new JobObject())
            {
                Assert.IsFalse(job.IsAssignedToProcess(Process.GetCurrentProcess()));
            }
        }

        [TestMethod]
        public void IsAssignedToProcess_Associated()
        {
            using (var job = new JobObject())
            {
                var process = Process.GetCurrentProcess();
                job.AssignProcess(process);

                Assert.IsTrue(job.IsAssignedToProcess(process));
            }
        }
    }
}
