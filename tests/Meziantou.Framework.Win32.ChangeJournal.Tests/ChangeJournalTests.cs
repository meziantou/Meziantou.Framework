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
        public static void ClassInitialize(TestContext _)
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Assert.Inconclusive("Current user is not in the administator group");
            }

            if (!string.IsNullOrEmpty(Environment.ExpandEnvironmentVariables("SYSTEM_DEFINITIONID")))
            {
                Assert.Inconclusive("Does not on CI");
            }
        }

        [TestMethod]
        public void EnumerateEntries_ShouldFindNewFile()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                var item = changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
                Assert.IsNull(item);

                File.WriteAllText(file, "test");
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileCreate)));
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.DataExtend)));
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

                File.Delete(file);
                Assert.IsNotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileDelete)));
            }
        }

        [TestMethod]
        public void GetEntries_ShouldFilterEntries()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using (var changeJournal = ChangeJournal.Open(new DriveInfo(drive)))
            {
                var item = changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
                Assert.IsNull(item);

                File.WriteAllText(file, "test");
                Assert.IsNull(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)));
                Assert.IsNotNull(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

                File.Delete(file);
            }
        }

        [TestMethod]
        public void EnumerateEntries_ShouldNotBeEmpty()
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
