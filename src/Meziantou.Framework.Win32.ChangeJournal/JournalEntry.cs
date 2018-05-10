using System;
using System.IO;
using Meziantou.Framework.Win32.Native.Journal;

namespace Meziantou.Framework.Win32
{
    public class JournalEntry
    {
        /// <summary>
        ///     Copy constructor.
        /// </summary>
        /// <param name="nativeEntry"></param>
        /// <param name="name"></param>
        internal JournalEntry(USN_RECORD_V2 nativeEntry, string name)
        {
            Name = name;
            Length = (int) nativeEntry.RecordLength;
            Version = new Version(nativeEntry.MajorVersion, nativeEntry.MinorVersion);
            ReferenceNumber = (long) nativeEntry.FileReferenceNumber;
            ParentReferenceNumber = (long) nativeEntry.ParentFileReferenceNumber;
            UniqueSequenceNumber = nativeEntry.USN;
            TimeStamp = DateTime.FromFileTimeUtc(nativeEntry.TimeStamp);
            Reason = (ChangeReason) nativeEntry.Reason;
            Source = (SourceInformation) nativeEntry.SourceInfo;
            SecurityID = (int) nativeEntry.SecurityId;
            Attributes = (FileAttributes) nativeEntry.FileAttributes;
        }

        /// <summary>
        ///     Gets the total length of the entry in bytes,
        ///     including the file name.
        /// </summary>
        public int Length { get; }

        /// <summary>
        ///     Gets the Change Journal software version
        ///     this entry was written by.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        ///     Gets the reference number of the file or
        ///     directory associated with this entry.
        /// </summary>
        public long ReferenceNumber { get; }

        /// <summary>
        ///     Gets the reference number of the parent of the file or
        ///     directory associated with this entry.
        /// </summary>
        public long ParentReferenceNumber { get; }

        /// <summary>
        ///     Gets the Unique Sequence Number of this entry.
        /// </summary>
        public long UniqueSequenceNumber { get; }

        /// <summary>
        ///     Gets thhe standard UTC time stamp of when this entry was written.
        /// </summary>
        public DateTime TimeStamp { get; }

        /// <summary>
        ///     The changes that were made to the associated file or
        ///     directory for the record to be entered into the journal.
        /// </summary>
        public ChangeReason Reason { get; }

        /// <summary>
        ///     The reason the changes were made to the associated file or
        ///     directory as specified by the source of the entry.
        /// </summary>
        public SourceInformation Source { get; }

        /// <summary>
        ///     The identifier the system uses to indentify the security
        ///     descriptor of the associated file or directory.
        /// </summary>
        public int SecurityID { get; }

        /// <summary>
        ///     The attributes of the associated file or directory.
        /// </summary>
        public FileAttributes Attributes { get; }

        /// <summary>
        ///     The name of the file or directory associated with this record.
        /// </summary>
        public string Name { get; }
    }
}