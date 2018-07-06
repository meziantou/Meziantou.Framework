using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.RelativeDate.Tests
{
    [TestClass]
    [DoNotParallelize] // because DateTimeService.Clock is static
    public class RelativeDateTests
    {
        [TestMethod]
        public void DateInTheFuture_ThrowNotSupportedException()
        {
            DateTimeService.Clock = new Clock(new DateTime(2018, 1, 1));
            Assert.ThrowsException<NotSupportedException>(() => new RelativeDate(new DateTime(2018, 1, 2)).ToString());
        }

        [DataTestMethod]
        [DynamicData(nameof(RelativeDate_ToString_Data), DynamicDataSourceType.Property)]
        public void RelativeDate_ToString(string dateTimeStr, string nowStr, DateTimeKind kind, string expectedValueEn, string expectedValueFr)
        {
            var now = DateTime.SpecifyKind(DateTime.Parse(nowStr, CultureInfo.InvariantCulture), kind);
            var dateTime = DateTime.SpecifyKind(DateTime.Parse(dateTimeStr, CultureInfo.InvariantCulture), kind);

            DateTimeService.Clock = new Clock(now);
            var relativeDate = new RelativeDate(dateTime);
            var resultEn = relativeDate.ToString(null, CultureInfo.InvariantCulture);
            Assert.AreEqual(expectedValueEn, resultEn);

            var resultEs = relativeDate.ToString(null, CultureInfo.GetCultureInfo("es-ES"));
            Assert.AreEqual(expectedValueEn, resultEs);

            var resultFr = relativeDate.ToString(null, CultureInfo.GetCultureInfo("fr"));
            Assert.AreEqual(expectedValueFr, resultFr);
        }

        private static IEnumerable<object[]> RelativeDate_ToString_Data
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
