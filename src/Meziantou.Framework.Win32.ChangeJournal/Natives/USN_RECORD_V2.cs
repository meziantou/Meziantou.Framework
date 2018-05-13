using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    // https://msdn.microsoft.com/en-us/library/windows/desktop/aa365722(v=vs.85).aspx
    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_RECORD_V2
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong FileReferenceNumber;
        public ulong ParentFileReferenceNumber;
        public Usn USN;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }
}