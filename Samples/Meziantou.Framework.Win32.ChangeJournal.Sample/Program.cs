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
foreach (var entry in entries.OfType<ChangeJournalEntryVersion4>())
{
    Console.WriteLine($"{entry.UniqueSequenceNumber}; file id: {entry.ReferenceNumber:X8}; reason: {entry.Reason}; remaining: {entry.RemainingExtents}");
    foreach (var extent in entry.Extents)
    {
        Console.WriteLine("  - " + extent);
    }
}
