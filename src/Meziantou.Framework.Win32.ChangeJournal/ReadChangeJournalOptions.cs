namespace Meziantou.Framework.Win32
{
    internal sealed class ReadChangeJournalOptions
    {
        public ReadChangeJournalOptions(Usn? initialUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
        {
            InitialUSN = initialUSN;
            ReasonFilter = reasonFilter;
            ReturnOnlyOnClose = returnOnlyOnClose;
            Timeout = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        }

        public Usn? InitialUSN { get; }
        public ChangeReason ReasonFilter { get; }
        public bool ReturnOnlyOnClose { get; }
        public TimeSpan Timeout { get; }
    }
}
