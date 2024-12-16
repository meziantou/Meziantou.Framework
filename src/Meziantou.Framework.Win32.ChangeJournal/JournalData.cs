namespace Meziantou.Framework.Win32;

public sealed class JournalData
{
    internal JournalData()
    {
    }

    /// <summary>
    ///     Copy constructor, creates an instance of this type,
    ///     populating elements from the corresponding structure.
    /// </summary>
    /// <param name="nativeData"></param>
    internal JournalData(Windows.Win32.System.Ioctl.USN_JOURNAL_DATA_V2 nativeData)
    {
        ID = nativeData.UsnJournalID;
        FirstUSN = nativeData.FirstUsn;
        NextUSN = nativeData.NextUsn;
        LowestValidUSN = nativeData.LowestValidUsn;
        MaximumUSN = nativeData.MaxUsn;
        MaximumSize = nativeData.MaximumSize;
        AllocationDelta = nativeData.AllocationDelta;
        MinSupportedMajorVersion = nativeData.MinSupportedMajorVersion;
        MaxSupportedMajorVersion = nativeData.MaxSupportedMajorVersion;
        Flags = (ChangeJournalFlags) nativeData.Flags;
        RangeTrackChunkSize = nativeData.RangeTrackChunkSize;
        RangeTrackFileSizeThreshold = nativeData.RangeTrackFileSizeThreshold;
    }

    /// <summary>
    ///     64-bit unique journal identifier.
    /// </summary>
    public ulong ID { get; }

    /// <summary>
    ///     Identifies the first Usn in the journal.
    ///     All USN's below this value have been purged.
    /// </summary>
    public Usn FirstUSN { get; }

    /// <summary>
    ///     The Usn that will be assigned to the next record appended to the journal.
    /// </summary>
    public Usn NextUSN { get; }

    /// <summary>
    ///     The lowest Usn that is valid for this journal and may be zero.
    ///     All changes with this Usn or higher have been recorded in the journal.
    /// </summary>
    public Usn LowestValidUSN { get; }

    /// <summary>
    ///     The largest Usn that will ever to assigned to a record in this journal.
    /// </summary>
    public Usn MaximumUSN { get; }

    /// <summary>
    ///     The maximum size, in bytes, the journal can use on the volume.
    /// </summary>
    public ulong MaximumSize { get; }

    /// <summary>
    ///     The size, in bytes, the journal can grow when needed, and
    ///     purge from the start of the journal is it grows past MaximumSize.
    /// </summary>
    public ulong AllocationDelta { get; }

    /// <summary>
    /// The minimum version of the USN change journal that the file system supports.
    /// </summary>
    public ushort MinSupportedMajorVersion { get; }

    /// <summary>
    /// The maximum version of the USN change journal that the file system supports.
    /// </summary>
    public ushort MaxSupportedMajorVersion { get; }

    /// <summary>
    /// Whether or not range tracking is turned on. The following are the possible values for the Flags member.
    /// </summary>
    public ChangeJournalFlags Flags { get; }

    /// <summary>
    /// The granularity of tracked ranges.
    /// Valid only when you also set the Flags member to <see cref="ChangeJournalFlags.TrackModifiedRangesEnable"/>.
    /// </summary>
    public ulong RangeTrackChunkSize { get; }

    /// <summary>
    /// File size threshold to start tracking range for files with equal or larger size.
    /// Valid only when you also set the Flags member to <see cref="ChangeJournalFlags.TrackModifiedRangesEnable"/>.
    /// </summary>
    public long RangeTrackFileSizeThreshold { get; }
}
