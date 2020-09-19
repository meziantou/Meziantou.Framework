using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
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
        internal JournalData(USN_JOURNAL_DATA nativeData)
        {
            ID = nativeData.UsnJournalID;
            FirstUSN = nativeData.FirstUsn;
            NextUSN = nativeData.NextUsn;
            LowestValidUSN = nativeData.LowestValidUsn;
            MaximumUSN = nativeData.MaxixmumUsn;
            MaximumSize = nativeData.MaximumSize;
            AllocationDelta = nativeData.AllocationDelta;
        }

        /// <summary>
        ///     64-bit unique journal identifier.
        /// </summary>
        public ulong ID { get; }

        /// <summary>
        ///     Identifies the first Usn in the journal.
        ///     All Usn's below this value have been purged.
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
    }
}
