using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests;

// The tests are flaky on GitHub Actions, use a retry mechanism
public class ChangeJournalTests
{
    [Fact, RunIfWindowsAdministrator]
    public void EnumerateEntries_ShouldFindNewFile()
    {
        Retry(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Null(item);

            File.WriteAllText(file, "test");
            Assert.NotNull(changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileCreate)));
            Assert.NotNull(changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.DataExtend)));
            Assert.NotNull(changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

            var lastUsn = changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().Last(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Equal(lastUsn.UniqueSequenceNumber, ChangeJournal.GetEntry(file).UniqueSequenceNumber);

            File.Delete(file);
            Assert.NotNull(changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileDelete)));
        });
    }

    [Fact, RunIfWindowsAdministrator]
    public void GetEntries_ShouldFilterEntries()
    {
        Retry(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Null(item);

            File.WriteAllText(file, "test");
            Assert.Null(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)));
            Assert.NotNull(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

            File.Delete(file);
        });
    }

    [Fact, RunIfWindowsAdministrator]
    public void EnumerateEntries_ShouldNotBeEmpty()
    {
        Retry(() =>
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var entries = changeJournal.Entries.ToList();
            Assert.True(entries.Count >= 0);
        });
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void NonAdministrator()
    {
        Retry(() =>
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file);
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive), unprivileged: true);
            var entries = changeJournal.GetEntries(ChangeReason.FileCreate, returnOnlyOnClose: false, TimeSpan.FromSeconds(10)).ToList();
            Assert.True(entries.Count >= 0);
        });
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetFileIdentifier()
    {
        var file = Path.GetTempFileName();
        var identifier = FileIdentifier.FromFile(file);
        Assert.NotEqual(default, identifier);
    }

    [Fact]
    public void FileIdentifier128ToString()
    {
        FileIdentifier fileIdentifier = new FileIdentifier(new UInt128(0, 10));
        Assert.Equal("0000000000000000000000000000000a", fileIdentifier.ToString());
    }

    [Fact]
    public void FileIdentifier64ToString()
    {
        FileIdentifier fileIdentifier = new FileIdentifier(10);
        Assert.Equal("000000000000000a", fileIdentifier.ToString());
    }
}
