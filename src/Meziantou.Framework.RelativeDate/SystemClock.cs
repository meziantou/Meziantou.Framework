using System;

namespace Meziantou.Framework
{
    internal class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;

        public DateTime UtcNow => DateTime.UtcNow;
    }
}
