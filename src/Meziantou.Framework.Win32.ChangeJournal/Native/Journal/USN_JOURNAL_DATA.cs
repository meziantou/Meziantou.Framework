using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Native.Journal
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_JOURNAL_DATA
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxixmumUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }
}