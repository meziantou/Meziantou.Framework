#nullable disable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ProcessEntry
    {
        internal ProcessEntry(int processId, int parentProcessId)
        {
            ProcessId = processId;
            ParentProcessId = parentProcessId;
        }

        public int ProcessId { get; }
        public int ParentProcessId { get; }

        public override bool Equals(object obj)
        {
            if (obj is ProcessEntry entry)
            {
                return ProcessId == entry.ProcessId &&
                      ParentProcessId == entry.ParentProcessId;
            }

            return false;
        }

        public override int GetHashCode()
        {
            var hashCode = 802333198;
            hashCode = hashCode * -1521134295 + ProcessId.GetHashCode();
            hashCode = hashCode * -1521134295 + ParentProcessId.GetHashCode();
            return hashCode;
        }

        public Process ToProcess()
        {
            return Process.GetProcessById(ProcessId);
        }
    }
}
