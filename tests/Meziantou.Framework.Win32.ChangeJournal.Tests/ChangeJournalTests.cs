using FluentAssertions;
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
            item.Should().BeNull();

            File.WriteAllText(file, "test");
            changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileCreate)).Should().NotBeNull();
            changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.DataExtend)).Should().NotBeNull();
            changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)).Should().NotBeNull();

            var lastUsn = changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().Last(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Equal(lastUsn.UniqueSequenceNumber, ChangeJournal.GetEntry(file).UniqueSequenceNumber);

            File.Delete(file);
            changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.FileDelete)).Should().NotBeNull();
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
            item.Should().BeNull();

            File.WriteAllText(file, "test");
            changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)).Should().BeNull();
            changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)).Should().NotBeNull();

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
            entries.Count.Should().BePositive();
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
            entries.Count.Should().BePositive();
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

    private static void Retry(Action action)
    {
        for (var i = 5; i >= 0; i--)
        {
            try
            {
                action();
                return;
            }
            catch when (i > 0)
            {
            }
        }
    }
}
