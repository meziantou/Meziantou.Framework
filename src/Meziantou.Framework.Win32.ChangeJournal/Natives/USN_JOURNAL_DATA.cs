using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_JOURNAL_DATA
    {
        public ulong UsnJournalID;
        public Usn FirstUsn;
        public Usn NextUsn;
        public Usn LowestValidUsn;
        public Usn MaxixmumUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }
}
