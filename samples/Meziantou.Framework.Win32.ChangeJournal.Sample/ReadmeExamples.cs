namespace Meziantou.Framework.Win32.ChangeJournalSample;

// Examples of the readme (src/Meziantou.Framework.Win32.ChangeJournal/readme.md) that wait forever or change the
// configuration of the volume. They are compiled in CI to keep the documentation in sync with the API, but not run.
public static class ReadmeExamples
{
    public static void MonitorChanges(ChangeJournal changeJournal)
    {
        #region ChangeJournal_MonitorChanges
        foreach (var change in changeJournal.GetEntries(ChangeReason.All, returnOnlyOnClose: false, Timeout.InfiniteTimeSpan))
        {
            if (change is ChangeJournalEntryVersion2or3 changev2)
            {
                Console.WriteLine($"{changev2.Name}: {changev2.Reason}");
            }
        }
        #endregion
    }

    public static void ConfigureJournal(ChangeJournal changeJournal)
    {
        #region ChangeJournal_Configure
        changeJournal.Create(maximumSize: 10_000_000, allocationDelta: 1_000_000);
        changeJournal.EnableTrackModifiedRanges(chunkSize: 10_000, fileSizeThreshold: 100_000_000);
        changeJournal.Delete();
        #endregion
    }
}
