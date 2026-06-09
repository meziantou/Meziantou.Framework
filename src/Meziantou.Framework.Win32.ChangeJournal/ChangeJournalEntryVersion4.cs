using Windows.Win32.System.Ioctl;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a change journal entry in version 4 format, which includes extent information for modified ranges.</summary>
public sealed class ChangeJournalEntryVersion4 : ChangeJournalEntry
{
    internal ChangeJournalEntryVersion4(USN_RECORD_V4 nativeEntry, ChangeJournalEntryExtent[] extents)
        : base(nativeEntry.Header.MajorVersion, nativeEntry.Header.MinorVersion)
    {
        ReferenceNumber = new FileIdentifier(nativeEntry.FileReferenceNumber);
        ParentReferenceNumber = new FileIdentifier(nativeEntry.ParentFileReferenceNumber);
        UniqueSequenceNumber = nativeEntry.Usn;
        Reason = (ChangeReason)nativeEntry.Reason;
        Source = (SourceInformation)nativeEntry.SourceInfo;
        RemainingExtents = nativeEntry.RemainingExtents;
        Extents = extents;
    }

    /// <summary>
    /// <para>The ordinal number of the file or directory for which this record notes changes. This is an arbitrarily assigned value that associates a journal record with a file.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public FileIdentifier ReferenceNumber { get; }

    /// <summary>
    /// <para>The ordinal number of the directory where the file or directory that is associated with this record is located. This is an arbitrarily assigned value that associates a journal record with a parent directory.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public FileIdentifier ParentReferenceNumber { get; }

    /// <summary>Gets the Unique Sequence Number of this entry.</summary>
    public Usn UniqueSequenceNumber { get; }

    /// <summary>
    /// <para>The flags that identify reasons for changes that have accumulated in this file or directory journal record since the file or directory opened. When a file or directory closes,
    /// then a final USN record is generated with the <c>USN_REASON_CLOSE</c> flag set. The next change (for example, after the next open operation or deletion) starts a new record with a new
    /// set of reason flags. A rename or move operation generates two USN records, one that records the old parent directory for the item, and one that records a new parent. The following table
    /// identifies the possible flags.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public ChangeReason Reason { get; }

    /// <summary>
    /// <para>Additional information about the source of the change, set by the <see href="https://docs.microsoft.com/windows/desktop/api/winioctl/ni-winioctl-fsctl_mark_handle">FSCTL_MARK_HANDLE</see> of
    /// the <see href="https://docs.microsoft.com/windows/desktop/api/ioapiset/nf-ioapiset-deviceiocontrol">DeviceIoControl</see> operation. When a thread writes a new USN record, the source information
    /// flags in the prior record continues to be present only if the thread also sets those flags.  Therefore, the source information structure allows applications to filter out USN records that are set
    /// only by a known source, for example, an antivirus filter. One of the two following values can be set.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public SourceInformation Source { get; }

    /// <summary>The number of extents that remain after the current <c>USN_RECORD_V4</c> record. Multiple version 4.0 records may be required to describe all of the modified extents for a given file.
    /// When the <c>RemainingExtents</c> member is 0, the current <c>USN_RECORD_V4</c> record is the last <c>USN_RECORD_V4</c> record for the file. The last <c>USN_RECORD_V4</c> entry for a given file is always
    /// followed by a <see href="https://docs.microsoft.com/windows/desktop/api/winioctl/ns-winioctl-usn_record_v3">USN_RECORD_V3</see> record with at least the <c>USN_REASON_CLOSE</c> flag set.</summary>
    public uint RemainingExtents { get; }

    /// <summary>An array of <see href="https://docs.microsoft.com/windows/desktop/api/winioctl/ns-winioctl-usn_record_extent">USN_RECORD_EXTENT</see> structures that represent the extents in the <c>USN_RECORD_V4</c> entry.</summary>
    public IReadOnlyList<ChangeJournalEntryExtent> Extents { get; }

    internal override Usn GetUsn() => UniqueSequenceNumber;
}
