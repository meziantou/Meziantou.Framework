# Meziantou.Framework.Win32.ChangeJournal

`Meziantou.Framework.Win32.ChangeJournal` helps you read the change journal of a volume. The change journal is a database that contains a list of every change made to the files or directories on the volume. You can use this library to monitor changes made to files and directories on the volume. You can also use it to manipulate the change journal (e.g. Create, Delete, enable change tracking).

## Get the last record for a file

```c#
var entry = ChangeJournal.GetEntry(@"D:\test.txt");
```

## Get all records

Use `TimeSpan.Zero` to read the records that are already in the journal and stop at the end of it.

```c#
using var changeJournal = ChangeJournal.Open(new DriveInfo("D:"), unprivileged: false);
foreach (var change in changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero))
{
    if (change is ChangeJournalEntryVersion2or3 changev2)
    {
    }
    else if (change is ChangeJournalEntryVersion4 changev4)
    {
    }
}
```

## Get all records from a USN

```c#
foreach (var change in changeJournal.GetEntries(entry.UniqueSequenceNumber, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero))
{
}
```

## Monitor changes as they happen

Pass a non-zero timeout to wait for new records instead of stopping at the end of the journal.
`Timeout.InfiniteTimeSpan` waits indefinitely, so the loop below never completes on its own.
The underlying control code counts in whole seconds, so a sub-second timeout is rounded up to one second.

```c#
foreach (var change in changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, Timeout.InfiniteTimeSpan))
{
}
```

# Create / Delete / Enable change tracking

```c#
changeJournal.Create(maximumSize: 10_000_000, allocationDelta: 1_000_000);
changeJournal.EnableTrackModifiedRanges(chunkSize: 10_000, fileSizeThreshold: 100_000_000);
changeJournal.Delete();
```
