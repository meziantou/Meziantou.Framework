namespace Meziantou.Framework
{
    public sealed class SingleInstanceEventArgs : EventArgs
    {
        public SingleInstanceEventArgs(int processId, string[] arguments)
        {
            ProcessId = processId;
            Arguments = arguments;
        }

        public int ProcessId { get; }

        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Cannot change the signature, breaking change")]
        public string[] Arguments { get; }
    }
}
