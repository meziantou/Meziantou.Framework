using System;
using System.IO;
using System.Linq;
using TestUtilities;
using FluentAssertions;

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
            item.Should().BeNull();

            File.WriteAllText(file, "test");
            changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileCreate)).Should().NotBeNull();
            changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.DataExtend)).Should().NotBeNull();
            changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)).Should().NotBeNull();

            File.Delete(file);
            changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileDelete)).Should().NotBeNull();
        }

        [RunIfWindowsAdministratorFact]
        public void GetEntries_ShouldFilterEntries()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            item.Should().BeNull();

            File.WriteAllText(file, "test");
            changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)).Should().BeNull();
            changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)).Should().NotBeNull();

            File.Delete(file);
        }

        [RunIfWindowsAdministratorFact]
        public void EnumerateEntries_ShouldNotBeEmpty()
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var entries = changeJournal.Entries.ToList();
            entries.Count.Should().BePositive();
        }
    }
}
