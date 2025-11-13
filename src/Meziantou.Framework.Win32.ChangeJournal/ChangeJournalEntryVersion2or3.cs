using System.Diagnostics;
using Windows.Win32.System.Ioctl;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a change journal entry in version 2 or version 3 format.</summary>
public sealed class ChangeJournalEntryVersion2or3 : ChangeJournalEntry
{
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

    internal ChangeJournalEntryVersion2or3(USN_RECORD_V2 entry, string name)
        : base(entry.MajorVersion, entry.MinorVersion)
    {
        UniqueSequenceNumber = entry.Usn;
        TimeStamp = DateTime.FromFileTimeUtc(entry.TimeStamp);
        Debug.Assert(TimeStamp.Kind is DateTimeKind.Utc);

        Reason = (ChangeReason)entry.Reason;
        Source = (SourceInformation)entry.SourceInfo;
        SecurityId = entry.SecurityId;
        Attributes = (FileAttributes)entry.FileAttributes;
        Name = name ?? throw new ArgumentNullException(nameof(name));

        ReferenceNumber = new FileIdentifier(entry.FileReferenceNumber);
        ParentReferenceNumber = new FileIdentifier(entry.ParentFileReferenceNumber);
    }
    
    internal ChangeJournalEntryVersion2or3(USN_RECORD_V3 entry, string name)
        : base(entry.MajorVersion, entry.MinorVersion)
    {
        UniqueSequenceNumber = entry.Usn;
        TimeStamp = DateTime.FromFileTimeUtc(entry.TimeStamp);
        Debug.Assert(TimeStamp.Kind is DateTimeKind.Utc);

        Reason = (ChangeReason)entry.Reason;
        Source = (SourceInformation)entry.SourceInfo;
        SecurityId = entry.SecurityId;
        Attributes = (FileAttributes)entry.FileAttributes;
        Name = name ?? throw new ArgumentNullException(nameof(name));

        ReferenceNumber = new FileIdentifier(entry.FileReferenceNumber);
        ParentReferenceNumber = new FileIdentifier(entry.ParentFileReferenceNumber);
    }

    /// <summary>Gets the Unique Sequence Number of this entry.</summary>
    public Usn UniqueSequenceNumber { get; }

    /// <summary>
    /// <para>The standard UTC time stamp (<a href="https://docs.microsoft.com/windows/desktop/api/minwinbase/ns-minwinbase-filetime">FILETIME</a>) of this record, in 64-bit format.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public DateTime TimeStamp { get; }

    /// <summary>
    /// <para>The flags that identify reasons for changes that have accumulated in this file or directory journal record since the file or directory opened. When a file or directory closes, then a final USN record is generated with the <b>USN_REASON_CLOSE</b> flag set. The next change (for example, after the next open operation or deletion) starts a new record with a new set of reason flags. A rename or move operation generates two USN records, one that records the old parent directory for the item, and one that records a new parent. The following  table identifies the possible flags. <div class="alert"><b>Note</b>  Unused bits are reserved.</div> <div> </div> </para>
    /// <para>This doc was truncated.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public ChangeReason Reason { get; }

    /// <summary>
    /// <para>Additional information about the source of the change, set by the <a href="https://docs.microsoft.com/windows/desktop/api/winioctl/ni-winioctl-fsctl_mark_handle">FSCTL_MARK_HANDLE</a> of the <a href="https://docs.microsoft.com/windows/desktop/api/ioapiset/nf-ioapiset-deviceiocontrol">DeviceIoControl</a> operation. When a thread writes a new USN record, the source information flags in the prior record continues to be present only if the thread also sets those flags.  Therefore, the source information structure allows applications to filter out USN records that are set only by a known source, for example, an antivirus filter. One of the two following values can be set. </para>
    /// <para>This doc was truncated.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public SourceInformation Source { get; }

    /// <summary>The unique security identifier assigned to the file or directory associated with this record.</summary>
    public uint SecurityId { get; }

    /// <summary>
    /// <para>The attributes for the file or directory associated with this record, as returned by the <a href="https://docs.microsoft.com/windows/desktop/api/fileapi/nf-fileapi-getfileattributesa">GetFileAttributes</a> function. Attributes of streams associated with the file or directory are excluded.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public FileAttributes Attributes { get; }

    /// <summary>
    /// <para>The name of the file or directory associated with this record in Unicode format. This file or directory name is of variable length. When working with <b>FileName</b>, do not count on the file name that contains a trailing '\0' delimiter, but instead determine the length of the file name by using <b>FileNameLength</b>. Do not perform any compile-time pointer arithmetic using <b>FileName</b>. Instead, make necessary calculations at run time by using the value of the <b>FileNameOffset</b> member. Doing so helps make your code compatible with any future versions of <b>USN_RECORD_V2</b>.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public string Name { get; }

    internal override Usn GetUsn() => UniqueSequenceNumber;

    public override string ToString()
    {
        return $"{Name} ({Reason})";
    }
}
