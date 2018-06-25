using System;
using System.Globalization;

namespace Meziantou.Framework.RelativeDate
{
    public readonly struct RelativeDate : IComparable, IComparable<RelativeDate>, IFormattable
    {
        private DateTime DateTime { get; }

        public RelativeDate(DateTime dateTime)
        {
            DateTime = dateTime;
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is RelativeDate rd)
                return CompareTo(rd);

            return CompareTo(default);
        }

        public int CompareTo(RelativeDate other)
        {
            return DateTime.CompareTo(other.DateTime);
        }

        public override string ToString()
        {
            return ToString(null, null);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            var now = DateTime.Kind == DateTimeKind.Utc ? DateTimeService.UtcNow : DateTimeService.Now;

            var delta = now - DateTime;
            if (delta < TimeSpan.Zero)
                throw new NotSupportedException("Dates in the future are not supported");

            var culture = formatProvider as CultureInfo;

            if (delta == TimeSpan.Zero)
                return GetString("Now", culture);

            if (delta < TimeSpan.FromMinutes(1))
                return delta.Seconds <= 1 ?
                    GetString("OneSecondAgo", culture) :
                    GetString("ManySecondsAgo", culture, delta.Seconds);

            if (delta < TimeSpan.FromMinutes(2))
                return GetString("AMinuteAgo", culture);

            if (delta < TimeSpan.FromMinutes(45))
                return GetString("ManyMinutesAgo", culture, delta.Minutes);

            if (delta < TimeSpan.FromMinutes(90))
                return GetString("AnHourAgo", culture);

            if (delta < TimeSpan.FromHours(24))
                return GetString("ManyHoursAgo", culture, delta.Hours);

            if (delta < TimeSpan.FromHours(48))
                return GetString("Yesterday", culture);

            if (delta < TimeSpan.FromDays(30))
                return GetString("ManyDaysAgo", culture, delta.Days);

            if (delta < TimeSpan.FromDays(365)) // We don't care about leap year
            {
                var months = Convert.ToInt32(Math.Floor((double)delta.Days / 30));
                return months <= 1 ?
                    GetString("OneMonthAgo", culture) :
                    GetString("ManyMonthsAgo", culture, months);
            }
            else
            {
                var years = Convert.ToInt32(Math.Floor((double)delta.Days / 365));
                return years <= 1 ?
                    GetString("OneYearAgo", culture) :
                    GetString("ManyYearsAgo", culture, years);
            }
        }

        private string GetString(string name, CultureInfo culture)
        {
            return LocalizationProvider.Current.GetString(name, culture);
        }

        private string GetString(string name, CultureInfo culture, int value)
        {
            return string.Format(LocalizationProvider.Current.GetString(name, culture), value);
        }
    }
}
