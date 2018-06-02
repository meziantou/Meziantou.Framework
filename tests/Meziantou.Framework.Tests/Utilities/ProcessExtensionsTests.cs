using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Meziantou.Framework.Utilities;
using System.Threading;
using System.ComponentModel;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class ProcessExtensionsTests
    {
        [TestMethod]
        public async Task RunAsTask()
        {
            var result = await ProcessExtensions.RunAsTask("cmd", "/C echo test", CancellationToken.None);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(1, result.Output.Count);
            Assert.AreEqual("test", result.Output[0].Text);
            Assert.AreEqual(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [TestMethod]
        public async Task RunAsTask_RedirectOutput()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C echo test";

            var result = await psi.RunAsTask(redirectOutput: true, CancellationToken.None);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(1, result.Output.Count);
            Assert.AreEqual("test", result.Output[0].Text);
            Assert.AreEqual(ProcessOutputType.StandardOutput, result.Output[0].Type);
        }

        [TestMethod]
        public async Task RunAsTask_DoNotRedirectOutput()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/C echo test";

            var result = await psi.RunAsTask(redirectOutput: false, CancellationToken.None);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(0, result.Output.Count);
        }

        [TestMethod]
        public async Task RunAsTask_ProcessDoesNotExists()
        {
            var psi = new ProcessStartInfo("ProcessDoesNotExists.exe");

            await Assert.ThrowsExceptionAsync<Win32Exception>(() => psi.RunAsTask(CancellationToken.None));
        }
    }
}
