using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Meziantou.Framework.RelativeDate.Tests")]

namespace Meziantou.Framework.RelativeDate
{
    internal class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;

        public DateTime UtcNow => DateTime.UtcNow;
    }
}
