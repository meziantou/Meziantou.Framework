using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace Meziantou.Framework.Win32.Tests
{
    [TestClass]
    public class ChangeJournalTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Assert.Inconclusive("Current user is not in the administator group");
            }
        }

        [TestMethod]
        public void ShouldFindNewFiles()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                var item = changeJournal.Entries.FirstOrDefault(entry => entry.Name == fileName);
                Assert.IsNull(item);
            }

            File.WriteAllText(file, "test");
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => entry.Name == fileName && entry.Reason.HasFlag(ChangeReason.FileCreate)));
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => entry.Name == fileName && entry.Reason.HasFlag(ChangeReason.DataExtend)));
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => entry.Name == fileName && entry.Reason.HasFlag(ChangeReason.Close)));
            }

            File.Delete(file);
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => entry.Name == fileName && entry.Reason.HasFlag(ChangeReason.FileDelete)));
            }
        }

        [TestMethod]
        public void EnumerateEntries()
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file);
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                var entries = changeJournal.Entries.ToList();
                Assert.IsTrue(entries.Count > 0);
            }
        }
    }
}
