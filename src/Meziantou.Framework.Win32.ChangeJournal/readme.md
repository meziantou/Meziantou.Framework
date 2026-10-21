# Meziantou.Framework.Win32.ChangeJournal

`Meziantou.Framework.Win32.ChangeJournal` helps you read the change journal of a volume. The change journal is a database that contains a list of every change made to the files or directories on the volume. You can use this library to monitor changes made to files and directories on the volume. You can also use it to manipulate the change journal (e.g. Create, Delete, enable change tracking).

The types are in the `Meziantou.Framework.Win32` namespace.

## Get the last record for a file

<!-- ref:../../tests/Meziantou.Framework.Win32.ChangeJournal.Tests/ChangeJournalTests.cs;region=ChangeJournal_GetEntry;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.GetTempFileName();
var entry = ChangeJournal.GetEntry(path);
Assert.Equal(Path.GetFileName(path), entry.Name);
```
<!-- endref -->

## Get all records

Use `TimeSpan.Zero` to read the records that are already in the journal and stop at the end of it. Reading the journal
with `unprivileged: false` requires administrator rights.

<!-- ref:../../tests/Meziantou.Framework.Win32.ChangeJournal.Tests/ChangeJournalTests.cs;region=ChangeJournal_GetEntries;format=md-fenced-code-block;dedent=true -->
```csharp
using var changeJournal = ChangeJournal.Open(new DriveInfo("C:"), unprivileged: false);

var names = new List<string>();
var modifiedRanges = new List<ChangeJournalEntryExtent>();
foreach (var change in changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero))
{
    if (change is ChangeJournalEntryVersion2or3 changev2)
    {
        names.Add(changev2.Name);
    }
    else if (change is ChangeJournalEntryVersion4 changev4)
    {
        // Version 4 records are written when the tracking of the modified ranges is enabled
        modifiedRanges.AddRange(changev4.Extents);
    }
}

Assert.NotEmpty(names);
```
<!-- endref -->

## Get all records from a USN

Read the records written after a given record, such as the one returned by `ChangeJournal.GetEntry`:

<!-- ref:../../tests/Meziantou.Framework.Win32.ChangeJournal.Tests/ChangeJournalTests.cs;region=ChangeJournal_GetEntriesFromUsn;format=md-fenced-code-block;dedent=true -->
```csharp
var changes = new List<ChangeJournalEntryVersion2or3>();
foreach (var change in changeJournal.GetEntries(entry.UniqueSequenceNumber, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero))
{
    if (change is ChangeJournalEntryVersion2or3 changev2)
    {
        changes.Add(changev2);
    }
}
```
<!-- endref -->

## Monitor changes as they happen

Pass a non-zero timeout to wait for new records instead of stopping at the end of the journal, so the loop below
never completes on its own. The timeout is how long the driver waits before it looks for new records again, not a
deadline: the read stays outstanding until a record matches the filter, whatever the timeout is. The underlying
control code counts in whole seconds, so a sub-second timeout is rounded up to one second.

The wait happens inside a synchronous call to the driver and cannot be cancelled, so only use a non-zero timeout on
a thread that can afford to block until a change happens.

<!-- ref:../../samples/Meziantou.Framework.Win32.ChangeJournal.Sample/ReadmeExamples.cs;region=ChangeJournal_MonitorChanges;format=md-fenced-code-block;dedent=true -->
```csharp
foreach (var change in changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, Timeout.InfiniteTimeSpan))
{
    if (change is ChangeJournalEntryVersion2or3 changev2)
    {
        Console.WriteLine($"{changev2.Name}: {changev2.Reason}");
    }
}
```
<!-- endref -->

# Create / Delete / Enable change tracking

<!-- ref:../../samples/Meziantou.Framework.Win32.ChangeJournal.Sample/ReadmeExamples.cs;region=ChangeJournal_Configure;format=md-fenced-code-block;dedent=true -->
```csharp
changeJournal.Create(maximumSize: 10_000_000, allocationDelta: 1_000_000);
changeJournal.EnableTrackModifiedRanges(chunkSize: 10_000, fileSizeThreshold: 100_000_000);
changeJournal.Delete();
```
<!-- endref -->
