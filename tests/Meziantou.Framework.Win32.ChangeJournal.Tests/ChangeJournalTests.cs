using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

// The tests are flaky on GitHub Actions, use a retry mechanism
public class ChangeJournalTests
{
    [Fact, RunIf(WindowsGroups.Administrator)]
    public void EnumerateEntries_ShouldFindNewFile()
    {
        Retry(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file) ?? throw new InvalidOperationException("Cannot determine drive root");
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

    [Fact, RunIf(WindowsGroups.Administrator)]
    public void GetEntries_ShouldFilterEntries()
    {
        Retry(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
            var fileName = Path.GetFileName(file);
            var drive = Path.GetPathRoot(file) ?? throw new InvalidOperationException("Cannot determine drive root");
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var item = changeJournal.Entries.OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal));
            Assert.Null(item);

            File.WriteAllText(file, "test");
            Assert.Null(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && !entry.Reason.HasFlag(ChangeReason.Close)));
            Assert.NotNull(changeJournal.GetEntries(ChangeReason.Close, returnOnlyOnClose: false, TimeSpan.Zero).OfType<ChangeJournalEntryVersion2or3>().FirstOrDefault(entry => string.Equals(entry.Name, fileName, StringComparison.Ordinal) && entry.Reason.HasFlag(ChangeReason.Close)));

            File.Delete(file);
        });
    }

    [Fact, RunIf(WindowsGroups.Administrator)]
    public void EnumerateEntries_ShouldNotBeEmpty()
    {
        Retry(() =>
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file) ?? throw new InvalidOperationException("Cannot determine drive root");
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive));
            var entries = changeJournal.Entries.ToList();
            Assert.NotEmpty(entries);
        });
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void NonAdministrator()
    {
        Retry(() =>
        {
            var file = Path.GetTempFileName();
            var drive = Path.GetPathRoot(file) ?? throw new InvalidOperationException("Cannot determine drive root");
            using var changeJournal = ChangeJournal.Open(new DriveInfo(drive), unprivileged: true);
            var entries = changeJournal.GetEntries(ChangeReason.FileCreate, returnOnlyOnClose: false, TimeSpan.FromSeconds(10)).ToList();
            Assert.NotEmpty(entries);
        });
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileIdentifier()
    {
        var file = Path.GetTempFileName();
        var identifier = FileIdentifier.FromFile(file);
        Assert.NotEqual(default, identifier);
    }

    [Theory]
    [InlineData(0, 0ul)]                    // do not wait, return at the end of the journal
    [InlineData(-1, uint.MaxValue)]         // Timeout.InfiniteTimeSpan
    [InlineData(-5000, uint.MaxValue)]      // any negative value waits indefinitely
    [InlineData(1, 1ul)]                    // sub-second values round up instead of becoming "do not wait"
    [InlineData(500, 1ul)]
    [InlineData(1000, 1ul)]
    [InlineData(1500, 2ul)]
    [InlineData(30_000, 30ul)]
    public void ReadOptions_ConvertsTimeoutToWholeSeconds(int milliseconds, ulong expected)
    {
        var options = new ReadChangeJournalOptions(initialUSN: null, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.FromMilliseconds(milliseconds), unprivileged: false);
        Assert.Equal(expected, options.TimeoutInSeconds);
    }

    [Theory]
    [InlineData(1L, 2L, -1)]
    [InlineData(2L, 1L, 1)]
    [InlineData(2L, 2L, 0)]
    [InlineData(-1L, 1L, -1)]
    public void UsnCompareTo(long left, long right, int expected)
    {
        Assert.Equal(expected, Math.Sign(new Usn(left).CompareTo(new Usn(right))));
        Assert.Equal(expected, Math.Sign(new Usn(left).CompareTo((object)new Usn(right))));
    }

    [Fact]
    public void UsnCompareToNullIsGreater()
    {
        Assert.Equal(1, new Usn(0).CompareTo(obj: null));
    }

    [Fact]
    public void UsnCanBeSorted()
    {
        var usns = new List<Usn> { new(30), new(10), new(20) };
        usns.Sort();
        Assert.Equal([new Usn(10), new Usn(20), new Usn(30)], usns);
    }

    [Fact]
    public void ChangeReasonAllIsTheUnionOfEveryOtherReason()
    {
        var expected = default(ChangeReason);
        foreach (var reason in Enum.GetValues<ChangeReason>())
        {
            if (reason is not ChangeReason.All)
            {
                expected |= reason;
            }
        }

        Assert.Equal(expected, ChangeReason.All);
    }

    [Fact]
    public void ChangeReasonAllMatchesTheNativeReasonMask()
    {
        Assert.Equal(0x80FFFF77u, (uint)ChangeReason.All);
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
