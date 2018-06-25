using System;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.RelativeDate
{
    internal static class DateTimeService
    {
        public static IClock Clock { get; set; } = new SystemClock();

        public static DateTime Now => Clock.Now;
        public static DateTime UtcNow => Clock.UtcNow;
    }
}
