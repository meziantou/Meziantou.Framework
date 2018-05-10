using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Native.Journal
{
    // https://msdn.microsoft.com/en-us/library/windows/desktop/aa363871.aspx
    [StructLayout(LayoutKind.Sequential)]
    internal struct CREATE_USN_JOURNAL_DATA
    {
        public long MaximumSize;
        public long AllocationDelta;
    }
}