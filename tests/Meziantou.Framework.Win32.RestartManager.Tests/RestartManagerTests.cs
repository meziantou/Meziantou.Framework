using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Tests
{
    [TestClass]
    public class RestartManagerTests
    {
        [TestMethod]
        public void GetProcessesLockingFile()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var processes = Meziantou.Framework.Win32.RestartManager.GetProcessesLockingFile(path);
                    CollectionAssert.AreEquivalent(new[] { Process.GetCurrentProcess().Id }, processes.Select(p => p.Id).ToList());
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void IsFileLocked_True()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var actual = Meziantou.Framework.Win32.RestartManager.IsFileLocked(path);
                    Assert.IsTrue(actual);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void IsFileLocked_False()
        {
            var path = Path.GetTempFileName();
            try
            {
                var actual = Meziantou.Framework.Win32.RestartManager.IsFileLocked(path);
                Assert.IsFalse(actual);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
