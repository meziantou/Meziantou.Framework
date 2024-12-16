using Meziantou.Framework;
using Meziantou.Framework.Win32;

using var changeJournal = ChangeJournal.Open(new DriveInfo("D:"), unprivileged: !Environment.IsPrivilegedProcess);

changeJournal.EnableTrackModifiedRanges(1, 1);
//changeJournal.Create(ByteSize.FromMegaBytes(10).Value, ByteSize.FromMegaBytes(1).Value);
File.Delete("D:/test.txt");
File.WriteAllBytes("D:/test.txt", new byte[ByteSize.FromMegaBytes(10).Value]);
var entries = changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, TimeSpan.FromSeconds(10)).ToList();
var lastUsn = entries.OfType<ChangeJournalEntryVersion2or3>().LastOrDefault()?.UniqueSequenceNumber;

Console.WriteLine($"Last USN: {lastUsn}");

using (var fs = new FileStream("D:/test.txt", FileMode.Open, FileAccess.Write))
{
    var buffer = new byte[] { 0xFF };
    for (int i = 0; i < 5; i++)
    {
        fs.Write(buffer);
        fs.Seek(1_000_000, SeekOrigin.Current);
    }

    fs.SetLength(ByteSize.FromMegaBytes(200).Value);
}

entries = changeJournal.GetEntries(lastUsn!.Value, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.FromSeconds(10)).ToList();
Console.WriteLine($"Entries: {entries.Count}");
foreach (var entry in entries)
{
    if (entry is ChangeJournalEntryVersion2or3 entry2or3)
    {
        Console.WriteLine($"{entry2or3.UniqueSequenceNumber}; version: {entry.Version}; file id: {entry2or3.ReferenceNumber:X8}; reason: {entry2or3.Reason}; name: {entry2or3.Name}");
    }
    else if (entry is ChangeJournalEntryVersion4 entry4)
    {
        Console.WriteLine($"{entry4.UniqueSequenceNumber}; version: {entry.Version}; file id: {entry4.ReferenceNumber:X8}; reason: {entry4.Reason}; remaining: {entry4.RemainingExtents}");
        foreach (var extent in entry4.Extents)
        {
            Console.WriteLine("  - " + extent);
        }
    }
}
