using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
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