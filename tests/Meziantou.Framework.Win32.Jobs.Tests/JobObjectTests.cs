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
    }
}
