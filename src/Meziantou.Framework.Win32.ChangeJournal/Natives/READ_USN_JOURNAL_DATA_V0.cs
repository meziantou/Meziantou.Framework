using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    // https://msdn.microsoft.com/en-us/library/windows/desktop/aa365481(v=vs.85).aspx
    [StructLayout(LayoutKind.Sequential)]
    internal struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public ChangeReason ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }
}