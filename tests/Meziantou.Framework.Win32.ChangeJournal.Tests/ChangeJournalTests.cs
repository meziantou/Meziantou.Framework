using System;
using System.IO;
using System.Linq;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    public class ChangeJournalTests
    {
        [RunIfWindowsAdministratorFact]
        public void EnumerateEntries_ShouldFindNewFile()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Null(item);

            File.WriteAllText(file, "test");
            Assert.NotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileCreate)));
            Assert.NotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.DataExtend)));
            Assert.NotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

            File.Delete(file);
            Assert.NotNull(changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileDelete)));
        }

        [RunIfWindowsAdministratorFact]
        public void GetEntries_ShouldFilterEntries()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Null(item);

            File.WriteAllText(file, "test");
            Assert.Null(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)));
            Assert.NotNull(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

            File.Delete(file);
        }

        [RunIfWindowsAdministratorFact]
        public void EnumerateEntries_ShouldNotBeEmpty()
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var entries = changeJournal.Entries.ToList();
            Assert.True(entries.Count > 0);
        }
    }
}
