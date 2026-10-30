---
title: Change journal
description: Read the NTFS change journal to find the files that changed on a volume, resume from a saved position, and watch new changes.
---

NTFS and ReFS volumes keep a change journal: a log of the changes made to the files and directories of the volume.
Each record gives the name of the file, its identifier, its parent directory and the reasons of the change, and has an
increasing update sequence number (USN). Reading the journal finds the files that changed since a given point without
scanning the volume. The `Meziantou.Framework.Win32.ChangeJournal` package reads and configures it.

```bash
dotnet add package Meziantou.Framework.Win32.ChangeJournal
```

## Get the last record of a file

[ChangeJournal.GetEntry](cref:Meziantou.Framework.Win32.ChangeJournal.GetEntry) returns the last record written for a file or a directory:

<!-- ref:../../tests/Meziantou.Framework.Win32.ChangeJournal.Tests/ChangeJournalTests.cs;region=ChangeJournal_GetEntry;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.GetTempFileName();
var entry = ChangeJournal.GetEntry(path);
Assert.Equal(Path.GetFileName(path), entry.Name);
```
<!-- endref -->

Its [UniqueSequenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.UniqueSequenceNumber) is the position of that record in the journal: comparing it with a saved value tells
whether the file changed since then. [FileIdentifier.FromFile](cref:Meziantou.Framework.Win32.FileIdentifier.FromFile) returns the identifier of a file, which stays the same
when the file is renamed or moved on the volume.

## Read the records of a volume

Open the journal of a drive, and enumerate its records:

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

- `unprivileged: false` reads the journal with the regular control code, which requires administrator rights. Pass
  `unprivileged: true` to read it from a process that is not elevated. In that mode, Windows returns the records
  without the name of the file: [Name](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.Name) is empty, so identify the files by their [ReferenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.ReferenceNumber), as in
  [Read the changes since the last run](#read-the-changes-since-the-last-run).
- [ChangeReason](cref:Meziantou.Framework.Win32.ChangeReason) filters the records: [ChangeReason.All](cref:Meziantou.Framework.Win32.ChangeReason.All), or a combination such as
  `ChangeReason.FileCreate | ChangeReason.FileDelete`. With `returnOnlyOnClose: true`, only the records written when the
  last handle to the file is closed are returned, which summarize the changes made while the file was open.
- `TimeSpan.Zero` stops at the end of the journal. Any other timeout waits for new records, see
  [Watch the changes](#watch-the-changes).
- The records are [ChangeJournalEntryVersion2or3](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3) instances, with the [Name](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.Name), [Reason](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.Reason), [Attributes](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.Attributes), [TimeStamp](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.TimeStamp),
  [ReferenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.ReferenceNumber) and [ParentReferenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.ParentReferenceNumber) of the file. [ChangeJournalEntryVersion4](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion4) records describe the ranges of
  a file that were modified; the volume only writes them when the tracking of the modified ranges is enabled.
- A record gives the name of the file, not its path. Use [ParentReferenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.ParentReferenceNumber) to find its directory.
- [Entries](cref:Meziantou.Framework.Win32.ChangeJournal.Entries) enumerates every record, like `GetEntries(ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero)`.

[ChangeJournal](cref:Meziantou.Framework.Win32.ChangeJournal) is disposable: it holds a handle to the volume.

## Read the changes since the last run

Save the position of the journal when the application stops, and read the records written after it the next time.
Check the journal first: when it was deleted and created again, or when the records were purged because the journal
reached its maximum size, some changes are lost, and the application must scan the files instead:

<!-- ref:../../tests/Meziantou.Framework.Win32.ChangeJournal.Tests/ChangeJournalTests.cs;region=ChangeJournal_ReadChangesSinceLastRun;format=md-fenced-code-block;dedent=true -->
```csharp
using var changeJournal = ChangeJournal.Open(drive, unprivileged: true);

// Save these values when the application stops, for instance in a file
var savedJournalId = changeJournal.Data.ID;
var savedUsn = changeJournal.Data.NextUSN;

// A change made while the application is not running
File.AppendAllText(path, "new content");

// When the application starts again, read the records written since the saved position
changeJournal.RefreshJournalData();
var changedFiles = new HashSet<FileIdentifier>();
if (changeJournal.Data.ID != savedJournalId || savedUsn < changeJournal.Data.FirstUSN)
{
    // The journal was recreated, or the records were purged: scan the files instead
}
else
{
    foreach (var change in changeJournal.GetEntries(savedUsn, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero))
    {
        // In unprivileged mode, the records have no name: identify the files by their reference number
        if (change is ChangeJournalEntryVersion2or3 entry)
        {
            changedFiles.Add(entry.ReferenceNumber);
        }
    }
}

Assert.Contains(FileIdentifier.FromFile(path), changedFiles);
```
<!-- endref -->

- The example reads the journal without administrator rights, so the records have no name. [FileIdentifier.FromFile](cref:Meziantou.Framework.Win32.FileIdentifier.FromFile)
  returns the reference number of a file from its path, to compare with the records.
- [Data](cref:Meziantou.Framework.Win32.ChangeJournal.Data) describes the journal when it was opened, and [RefreshJournalData](cref:Meziantou.Framework.Win32.ChangeJournal.RefreshJournalData) reads it again. [ID](cref:Meziantou.Framework.Win32.JournalData.ID) identifies the journal,
  [FirstUSN](cref:Meziantou.Framework.Win32.JournalData.FirstUSN) is the oldest record still in the journal and [NextUSN](cref:Meziantou.Framework.Win32.JournalData.NextUSN) the position of the next record.
- [Usn](cref:Meziantou.Framework.Win32.Usn) wraps the 64-bit value of a position: store its [Value](cref:Meziantou.Framework.Win32.Usn.Value), and create it again with `new Usn(value)`.
- [GetEntries](cref:Meziantou.Framework.Win32.ChangeJournal.GetEntries) throws an `ArgumentOutOfRangeException` when the position is outside the journal.

To read the records written after a record of a file, start from its [UniqueSequenceNumber](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion2or3.UniqueSequenceNumber):

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

## Watch the changes

With a timeout other than `TimeSpan.Zero`, the enumeration waits for new records at the end of the journal instead of
ending, so the loop never completes on its own:

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

- The timeout is how long Windows waits before it looks for new records again, not a deadline: the read stays pending
  until a record matches the filter. Windows counts it in whole seconds, so a shorter timeout is rounded up to one
  second.
- The wait happens in a synchronous call to Windows and cannot be cancelled. Run this loop on a dedicated thread that
  can stay blocked until a change happens.

## Configure the journal

These operations change the volume and require administrator rights:

<!-- ref:../../samples/Meziantou.Framework.Win32.ChangeJournal.Sample/ReadmeExamples.cs;region=ChangeJournal_Configure;format=md-fenced-code-block;dedent=true -->
```csharp
changeJournal.Create(maximumSize: 10_000_000, allocationDelta: 1_000_000);
changeJournal.EnableTrackModifiedRanges(chunkSize: 10_000, fileSizeThreshold: 100_000_000);
changeJournal.Delete();
```
<!-- endref -->

- [Create](cref:Meziantou.Framework.Win32.ChangeJournal.Create) creates the journal, or changes the maximum size and the allocation delta of the existing one. When a volume
  has no journal, `Data.IsActive` is `false` and reading records throws a `Win32Exception`.
- [EnableTrackModifiedRanges](cref:Meziantou.Framework.Win32.ChangeJournal.EnableTrackModifiedRanges) makes the volume write [ChangeJournalEntryVersion4](cref:Meziantou.Framework.Win32.ChangeJournalEntryVersion4) records, with the ranges of the files
  that were modified. `chunkSize` is the granularity of the ranges, and only the files of at least `fileSizeThreshold`
  bytes are tracked.
- [Delete](cref:Meziantou.Framework.Win32.ChangeJournal.Delete) deletes the journal and waits until the deletion completes. Windows has to visit every file of the volume, so
  it can take a long time; `Delete(waitForCompletion: false)` starts the deletion and returns. While the deletion runs,
  the other operations on the journal fail.
- Other applications, such as backup tools and search indexers, may rely on the journal: think twice before deleting it
  or reducing its size.
