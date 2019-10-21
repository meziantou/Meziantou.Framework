#nullable disable
using System;

namespace Meziantou.Framework
{
    public sealed class SingleInstanceEventArgs : EventArgs
    {
        public SingleInstanceEventArgs(int processId, string[] arguments)
        {
            ProcessId = processId;
            Arguments = arguments;
        }

        public int ProcessId { get; set; }

        public string[] Arguments { get; }
    }
}
