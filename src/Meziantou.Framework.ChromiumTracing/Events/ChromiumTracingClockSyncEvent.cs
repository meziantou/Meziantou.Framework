using System;

namespace Meziantou.Framework.ChromiumTracing
{
    public sealed class ChromiumTracingClockSyncEvent : ChromiumTracingEvent
    {
        public override string Type => "c";

        public override string? Name
        {
            get => "clock_sync";
            set
            {
                if (value != "clock_sync")
                    throw new InvalidOperationException("Name is not settable for a Clock Sync event");
            }
        }
    }
}
