using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Scheduling.Tests
{
    public partial class RecurrenceRuleTests
    {
        [TestMethod]
        public void Daily_EveryDayInJanuaryFor3years()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");
            var startDate = new DateTime(1998, 01, 01, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            var expectedDates = new List<DateTime>();
            for (var year = 0; year < 3; year++)
            {
                for (var day = 1; day <= 31; day++)
                {
                    expectedDates.Add(new DateTime(1998 + year, 01, day, 09, 00, 00));
                }
            }

            AssertOccurrences(occurrences, expectedDates.ToArray());
        }
    }
}
