﻿using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    [Collection("RelativeDateTests")] // because DateTimeService.Clock is static
    public class RelativeDateTests
    {
        [Fact]
        public void DateInTheFuture_ThrowNotSupportedException()
        {
            DateTimeService.Clock = new Clock(new DateTime(2018, 1, 1));
            Assert.Throws<NotSupportedException>(() => new RelativeDate(new DateTime(2018, 1, 2)).ToString());
        }

        [Fact]
        public void DefaultDate_ToString()
        {
            DateTimeService.Clock = new Clock(new DateTime(2018, 1, 1));
            var result = new RelativeDate(default).ToString();
            Assert.Equal("2018 years ago", result);
        }

        [Theory]
        [MemberData(nameof(RelativeDate_ToString_Data))]
        public void RelativeDate_ToString(string dateTimeStr, string nowStr, DateTimeKind kind, string expectedValueEn, string expectedValueFr)
        {
            var now = DateTime.SpecifyKind(DateTime.Parse(nowStr, CultureInfo.InvariantCulture), kind);
            var dateTime = DateTime.SpecifyKind(DateTime.Parse(dateTimeStr, CultureInfo.InvariantCulture), kind);

            DateTimeService.Clock = new Clock(now);
            var relativeDate = new RelativeDate(dateTime);
            var resultEn = relativeDate.ToString(format: null, CultureInfo.InvariantCulture);
            Assert.Equal(expectedValueEn, resultEn);

            var resultEs = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("es-ES"));
            Assert.Equal(expectedValueEn, resultEs);

            var resultFr = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("fr"));
            Assert.Equal(expectedValueFr, resultFr);
        }

        public static IEnumerable<object[]> RelativeDate_ToString_Data
        {
            get
            {
                foreach (var kind in new[] { DateTimeKind.Utc, DateTimeKind.Local })
                {
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 00:00:00", kind, "now", "maintenant" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 00:00:01", kind, "one second ago", "il y a une seconde" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 00:00:25", kind, "25 seconds ago", "il y a 25 secondes" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 00:01:00", kind, "a minute ago", "il y a une minute" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 00:10:00", kind, "10 minutes ago", "il y a 10 minutes" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 01:00:00", kind, "an hour ago", "il y a une heure" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/01 02:00:00", kind, "2 hours ago", "il y a 2 heures" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/02 00:00:00", kind, "yesterday", "hier" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/01/03 00:00:00", kind, "2 days ago", "il y a 2 jours" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/02/01 00:00:00", kind, "one month ago", "il y a un mois" };
                    yield return new object[] { "2018/01/01 00:00:00", "2018/04/01 00:00:00", kind, "3 months ago", "il y a 3 mois" };
                    yield return new object[] { "2018/01/01 00:00:00", "2019/01/01 00:00:00", kind, "one year ago", "il y a un an" };
                    yield return new object[] { "2018/01/01 00:00:00", "2021/01/01 00:00:00", kind, "3 years ago", "il y a 3 ans" };
                }
            }
        }

        private sealed class Clock : IClock
        {
            public Clock(DateTime dateTime)
            {
                Now = new DateTime(dateTime.Ticks, DateTimeKind.Local);
                UtcNow = new DateTime(dateTime.Ticks, DateTimeKind.Utc);
            }

            public DateTime Now { get; }
            public DateTime UtcNow { get; }
        }
    }
}
