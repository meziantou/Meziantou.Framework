using System;

namespace Meziantou.Framework.Win32
{
    internal class ReadChangeJournalOptions
    {
        public ReadChangeJournalOptions(long initialUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
        {
            InitialUSN = initialUSN;
            ReasonFilter = reasonFilter;
            ReturnOnlyOnClose = returnOnlyOnClose;
            Timeout = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        }

        public long InitialUSN { get; }
        public ChangeReason ReasonFilter { get; }
        public bool ReturnOnlyOnClose { get; }
        public TimeSpan Timeout { get; }
    }
}