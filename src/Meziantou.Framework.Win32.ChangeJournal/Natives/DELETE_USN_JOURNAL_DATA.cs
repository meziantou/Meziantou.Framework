using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    // https://msdn.microsoft.com/en-us/library/windows/desktop/aa363928.aspx
    [StructLayout(LayoutKind.Sequential)]
    internal struct DELETE_USN_JOURNAL_DATA
    {
        public ulong UsnJournalID;
        public DeletionFlag DeleteFlags;
    }
}
