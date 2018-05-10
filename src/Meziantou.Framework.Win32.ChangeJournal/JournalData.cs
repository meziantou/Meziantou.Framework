namespace Meziantou.Framework.Win32
{
    public class JournalData
    {
        internal JournalData()
        {
        }

        /// <summary>
        ///     Copy constructor, creates an instance of this type,
        ///     populating elements from the corresponding structure.
        /// </summary>
        /// <param name="nativeData"></param>
        internal JournalData(Native.Journal.USN_JOURNAL_DATA nativeData)
            : this()
        {
            ID = (long)nativeData.UsnJournalID;
            FirstUSN = nativeData.FirstUsn;
            NextUSN = nativeData.NextUsn;
            LowestValidUSN = nativeData.LowestValidUsn;
            MaximumUSN = nativeData.MaxixmumUsn;
            MaximumSize = (long)nativeData.MaximumSize;
            AllocationDelta = (long)nativeData.AllocationDelta;
        }

        /// <summary>
        ///     Gets a value indicating whether a journal is active, or not.
        /// </summary>
        public bool IsActive { get; internal set; }

        /// <summary>
        ///     Gets a value indicating whether the end of the journal has
        ///     been reached by reading, or not.
        /// </summary>
        public bool AtEndOfJournal { get; internal set; }

        /// <summary>
        ///     64-bit unique journal identifier.
        /// </summary>
        public long ID { get; }

        /// <summary>
        ///     Identifies the first Usn in the journal.
        ///     All Usn's below this value have been purged.
        /// </summary>
        public long FirstUSN { get; }

        /// <summary>
        ///     The Usn that will be assigned to the next record appended to the journal.
        /// </summary>
        public long NextUSN { get; }

        public long CurrentUSN { get; internal set; }

        /// <summary>
        ///     The lowest Usn that is valid for this journal and may be zero.
        ///     All changes with this Usn or higher have been recorded in the journal.
        /// </summary>
        public long LowestValidUSN { get; }

        /// <summary>
        ///     The largest Usn that will ever to assigned to a record in this journal.
        /// </summary>
        public long MaximumUSN { get; }

        /// <summary>
        ///     The maximum size, in bytes, the journal can use on the volume.
        /// </summary>
        public long MaximumSize { get; }

        /// <summary>
        ///     The size, in bytes, the journal can grow when needed, and
        ///     purge from the start of the journal is it grows past MaximumSize.
        /// </summary>
        public long AllocationDelta { get; }
    }
}