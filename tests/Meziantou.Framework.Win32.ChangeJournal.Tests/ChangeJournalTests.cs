using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Xunit;
using Windows.Win32.System.Ioctl;

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

            // TimeSpan.Zero stops at the end of the journal. Any other value keeps the read waiting for a new record,
            // so enumerating to the end would never return.
            var entries = changeJournal.GetEntries(ChangeReason.FileCreate, returnOnlyOnClose: false, TimeSpan.Zero).ToList();
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFileIdentifierOfADirectory()
    {
        var directory = CreateTemporaryDirectory();
        Assert.NotEqual(default, FileIdentifier.FromFile(directory));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetEntryOfADirectory()
    {
        var directory = CreateTemporaryDirectory();

        var entry = ChangeJournal.GetEntry(directory);

        Assert.Equal(Path.GetFileName(directory), entry.Name);
        Assert.True(entry.Attributes.HasFlag(FileAttributes.Directory));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        return directory;
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
    [InlineData(0, 0ul)]                    // do not wait, so the control code must not be asked for new data
    [InlineData(-1, 1ul)]                   // Timeout.InfiniteTimeSpan
    [InlineData(500, 1ul)]
    [InlineData(30_000, 1ul)]
    public void ReadOptions_AsksForNewDataOnlyWhenTheReadIsMeantToWait(int milliseconds, ulong expected)
    {
        // FSCTL_READ_USN_JOURNAL ignores its timeout while BytesToWaitFor is 0, so a read that is meant to wait has to
        // ask for at least one byte of new data.
        var options = new ReadChangeJournalOptions(initialUSN: null, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.FromMilliseconds(milliseconds), unprivileged: false);
        Assert.Equal(expected, options.BytesToWaitFor);
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
        var union = default(ChangeReason);
        foreach (var reason in Enum.GetValues<ChangeReason>())
        {
            if (reason is not ChangeReason.All)
            {
                union |= reason;
            }
        }

        Assert.Equal(ChangeReason.All, union);
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

    [Fact]
    public void GetBufferedEntry_ParsesAVersion2Record()
    {
        var buffer = CreateVersion2Record("test.txt");

        var entry = Assert.IsType<ChangeJournalEntryVersion2or3>(ParseRecord(buffer));

        Assert.Equal(new Version(2, 0), entry.Version);
        Assert.Equal("test.txt", entry.Name);
        Assert.Equal(new Usn(4096), entry.UniqueSequenceNumber);
        Assert.Equal(ChangeReason.FileCreate, entry.Reason);
        Assert.Equal(SourceInformation.AuxiliaryData, entry.Source);
        Assert.Equal(7u, entry.SecurityId);
        Assert.Equal(FileAttributes.Archive, entry.Attributes);
        Assert.Equal(RecordTimeStamp, entry.TimeStamp);
        Assert.Equal(new FileIdentifier(0x1122334455667788), entry.ReferenceNumber);
    }

    [Fact]
    public void GetBufferedEntry_ParsesAVersion3Record()
    {
        var buffer = CreateVersion3Record("example.log");

        var entry = Assert.IsType<ChangeJournalEntryVersion2or3>(ParseRecord(buffer));

        Assert.Equal(new Version(3, 0), entry.Version);
        Assert.Equal("example.log", entry.Name);
        Assert.Equal(new Usn(8192), entry.UniqueSequenceNumber);
        Assert.Equal(ChangeReason.RenameNewName, entry.Reason);
        Assert.Equal(RecordTimeStamp, entry.TimeStamp);
    }

    [Fact]
    public void GetBufferedEntry_ParsesAVersion4Record()
    {
        var buffer = CreateVersion4Record(extentCount: 2);

        var entry = Assert.IsType<ChangeJournalEntryVersion4>(ParseRecord(buffer));

        Assert.Equal(new Version(4, 0), entry.Version);
        Assert.Equal(new Usn(16384), entry.UniqueSequenceNumber);
        Assert.Equal(ChangeReason.DataOverwrite, entry.Reason);
        Assert.Equal(3u, entry.RemainingExtents);
        Assert.Equal(2, entry.Extents.Count);
        Assert.Equal(1000, entry.Extents[0].Offset);
        Assert.Equal(2000, entry.Extents[0].Length);
        Assert.Equal(1001, entry.Extents[1].Offset);
        Assert.Equal(2001, entry.Extents[1].Length);
    }

    [Fact]
    public void GetBufferedEntry_RejectsAFileNamePastTheEndOfTheRecord()
    {
        var buffer = CreateVersion2Record("test.txt");

        // The name is stored inside the record, so a length that runs past the record cannot be trusted.
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(56), 4096);

        Assert.Throws<InvalidDataException>(() => ParseRecord(buffer));
    }

    [Fact]
    public void GetBufferedEntry_RejectsExtentsPastTheEndOfTheRecord()
    {
        var buffer = CreateVersion4Record(extentCount: 2);

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(60), 512);

        Assert.Throws<InvalidDataException>(() => ParseRecord(buffer));
    }

    [Fact]
    public void GetBufferedEntry_RejectsAnExtentSmallerThanTheNativeStructure()
    {
        var buffer = CreateVersion4Record(extentCount: 2);

        // An extent smaller than USN_RECORD_EXTENT would make the stride walk into the middle of the extents.
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(62), 8);

        Assert.Throws<InvalidDataException>(() => ParseRecord(buffer));
    }

    [Theory]
    [InlineData(2, 40)]     // USN_RECORD_V2 is 64 bytes
    [InlineData(3, 70)]     // USN_RECORD_V3 is 80 bytes
    [InlineData(4, 40)]     // USN_RECORD_V4 is 80 bytes
    public void GetBufferedEntry_RejectsARecordSmallerThanTheStructureItDeclares(int majorVersion, int recordLength)
    {
        var buffer = majorVersion switch
        {
            2 => CreateVersion2Record("test.txt"),
            3 => CreateVersion3Record("example.log"),
            _ => CreateVersion4Record(extentCount: 0),
        };

        // A record only long enough for the common header still gets marshalled as its full versioned structure, which reads
        // past the record unless the length is checked against that structure.
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), (uint)recordLength);

        // The name and extent guards would reject these records too, but only after the structure has been read out of them.
        // Matching the message is what proves the length was rejected first.
        var exception = Assert.Throws<InvalidDataException>(() => ParseRecord(buffer));
        Assert.Contains("smaller than the", exception.Message);
    }

    [Fact]
    public void GetBufferedEntry_RejectsAnUnsupportedRecordVersion()
    {
        var buffer = CreateVersion2Record("test.txt");

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 5);

        Assert.Throws<NotSupportedException>(() => ParseRecord(buffer));
    }

    private static readonly DateTime RecordTimeStamp = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>
    ///     Records are laid out inside a larger buffer, so the synthetic ones are too. Writing them into a buffer that is bigger
    ///     than the record keeps a test that deliberately declares a bad length from reading past the array.
    /// </summary>
    private const int RecordBufferLength = 512;

    private const int Version2FileNameOffset = 60;
    private const int Version3FileNameOffset = 76;
    private const int Version4ExtentsOffset = 64;

    private static byte[] CreateVersion2Record(string name)
    {
        var buffer = new byte[RecordBufferLength];
        var nameLength = name.Length * sizeof(char);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), (uint)AlignRecordLength(Version2FileNameOffset + nameLength));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), 0x1122334455667788);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16), 0x8877665544332211);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(24), 4096);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(32), RecordTimeStamp.ToFileTimeUtc());
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(40), (uint)ChangeReason.FileCreate);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(44), (uint)SourceInformation.AuxiliaryData);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(48), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(52), (uint)FileAttributes.Archive);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(56), (ushort)nameLength);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(58), Version2FileNameOffset);
        WriteName(buffer, Version2FileNameOffset, name);
        return buffer;
    }

    private static byte[] CreateVersion3Record(string name)
    {
        var buffer = new byte[RecordBufferLength];
        var nameLength = name.Length * sizeof(char);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), (uint)AlignRecordLength(Version3FileNameOffset + nameLength));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), 0x1122334455667788);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24), 0x8877665544332211);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(40), 8192);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(48), RecordTimeStamp.ToFileTimeUtc());
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(56), (uint)ChangeReason.RenameNewName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(60), (uint)SourceInformation.SourceInfoNotSpecified);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(64), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(68), (uint)FileAttributes.Normal);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(72), (ushort)nameLength);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(74), Version3FileNameOffset);
        WriteName(buffer, Version3FileNameOffset, name);
        return buffer;
    }

    private static byte[] CreateVersion4Record(int extentCount, int extentSize = 16)
    {
        var buffer = new byte[RecordBufferLength];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), (uint)(Version4ExtentsOffset + (extentCount * extentSize)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), 0x1122334455667788);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24), 0x8877665544332211);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(40), 16384);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(48), (uint)ChangeReason.DataOverwrite);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(52), (uint)SourceInformation.SourceInfoNotSpecified);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(60), (ushort)extentCount);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(62), (ushort)extentSize);

        for (var i = 0; i < extentCount; i++)
        {
            var offset = Version4ExtentsOffset + (i * extentSize);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), 1000 + i);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset + 8), 2000 + i);
        }

        return buffer;
    }

    private static void WriteName(byte[] buffer, int offset, string name)
    {
        MemoryMarshal.AsBytes(name.AsSpan()).CopyTo(buffer.AsSpan(offset));
    }

    // The change journal aligns records on 8 byte boundaries.
    private static int AlignRecordLength(int length) => (length + 7) & ~7;

    private static ChangeJournalEntry ParseRecord(byte[] buffer)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            var header = Marshal.PtrToStructure<USN_RECORD_COMMON_HEADER>(pointer);
            return ChangeJournalEntries.GetBufferedEntry(pointer, header);
        }
        finally
        {
            handle.Free();
        }
    }
}
