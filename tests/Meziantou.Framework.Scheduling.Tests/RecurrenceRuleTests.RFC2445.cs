using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Scheduling.Tests
{
    [TestClass]
    public partial class RecurrenceRuleTests
    {
        private static void AssertOccurrencesStartWith(IEnumerable<DateTime> occurrences, params DateTime[] expectedOccurrences)
        {
            AssertOccurrences(occurrences, false, null, expectedOccurrences);
        }

        private static void AssertOccurrences(IEnumerable<DateTime> occurrences, params DateTime[] expectedOccurrences)
        {
            AssertOccurrences(occurrences, false, expectedOccurrences.Length, expectedOccurrences);
        }

        private static void AssertOccurrences(IEnumerable<DateTime> occurrences, bool checkEnd, int? maxOccurences, params DateTime[] expectedOccurrences)
        {
            var occurrenceCount = 0;
            using (var enumerator1 = occurrences.GetEnumerator())
            {
                using (var enumerator2 = ((IEnumerable<DateTime>)expectedOccurrences).GetEnumerator())
                {
                    while (enumerator1.MoveNext() && enumerator2.MoveNext())
                    {
                        occurrenceCount++;
                        Assert.AreEqual(enumerator2.Current, enumerator1.Current);
                    }
                }

                if (maxOccurences.HasValue)
                {
                    while (enumerator1.MoveNext())
                    {
                        if (maxOccurences > occurrenceCount)
                        {
                            Assert.Fail("There are more occurences than expected.");
                        }

                        occurrenceCount++;
                    }
                }
                else
                {
                    if (checkEnd && !enumerator1.MoveNext())
                    {
                        Assert.Fail("There are more occurences than expected.");
                    }
                }
            }
        }

        [TestMethod]
        public void Daily_For3ccurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 03, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_WithUntil()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=19970904T070000Z");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 03, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_Forever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 03, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_Interval()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=2");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00),
                new DateTime(1997, 09, 06, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_WithInterval_Until1()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=2;UNTIL=19970904T070000Z");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_WithInterval_Until2()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=2;UNTIL=19970905T070000Z");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_For10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=10");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 09, 09, 00, 00),
                new DateTime(1997, 09, 16, 09, 00, 00),
                new DateTime(1997, 09, 23, 09, 00, 00),
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 07, 09, 00, 00),
                new DateTime(1997, 10, 14, 09, 00, 00),
                new DateTime(1997, 10, 21, 09, 00, 00),
                new DateTime(1997, 10, 28, 09, 00, 00),
                new DateTime(1997, 11, 04, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_Until_1997_12_24()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;UNTIL=19971224T000000Z");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 09, 09, 00, 00),
                new DateTime(1997, 09, 16, 09, 00, 00),
                new DateTime(1997, 09, 23, 09, 00, 00),
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 07, 09, 00, 00),
                new DateTime(1997, 10, 14, 09, 00, 00),
                new DateTime(1997, 10, 21, 09, 00, 00),
                new DateTime(1997, 10, 28, 09, 00, 00),
                new DateTime(1997, 11, 04, 09, 00, 00),
                new DateTime(1997, 11, 11, 09, 00, 00),
                new DateTime(1997, 11, 18, 09, 00, 00),
                new DateTime(1997, 11, 25, 09, 00, 00),
                new DateTime(1997, 12, 02, 09, 00, 00),
                new DateTime(1997, 12, 09, 09, 00, 00),
                new DateTime(1997, 12, 16, 09, 00, 00),
                new DateTime(1997, 12, 23, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_EveryOtherWeekForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;WKST=SU");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 16, 09, 00, 00),
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 14, 09, 00, 00),
                new DateTime(1997, 10, 28, 09, 00, 00),
                new DateTime(1997, 11, 11, 09, 00, 00),
                new DateTime(1997, 11, 25, 09, 00, 00),
                new DateTime(1997, 12, 09, 09, 00, 00),
                new DateTime(1997, 12, 23, 09, 00, 00),
                new DateTime(1998, 01, 06, 09, 00, 00),
                new DateTime(1998, 01, 20, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_TuesdayAndThursdayFor5Weeks()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;UNTIL=19971007T000000Z;WKST=SU;BYDAY=TU,TH");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 04, 09, 00, 00),
                new DateTime(1997, 09, 09, 09, 00, 00),
                new DateTime(1997, 09, 11, 09, 00, 00),
                new DateTime(1997, 09, 16, 09, 00, 00),
                new DateTime(1997, 09, 18, 09, 00, 00),
                new DateTime(1997, 09, 23, 09, 00, 00),
                new DateTime(1997, 09, 25, 09, 00, 00),
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 02, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_WeekStart01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=MO");
            var startDate = new DateTime(1997, 08, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 08, 05, 09, 00, 00),
                new DateTime(1997, 08, 10, 09, 00, 00),
                new DateTime(1997, 08, 19, 09, 00, 00),
                new DateTime(1997, 08, 24, 09, 00, 00));
        }

        [TestMethod]
        public void Weekly_WeekStart02()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=SU");
            var startDate = new DateTime(1997, 08, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 08, 05, 09, 00, 00),
                new DateTime(1997, 08, 17, 09, 00, 00),
                new DateTime(1997, 08, 19, 09, 00, 00),
                new DateTime(1997, 08, 31, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_1stFridayForTenOccurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=10;BYDAY=1FR");
            var startDate = new DateTime(1997, 09, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 05, 09, 00, 00),
                new DateTime(1997, 10, 03, 09, 00, 00),
                new DateTime(1997, 11, 07, 09, 00, 00),
                new DateTime(1997, 12, 05, 09, 00, 00),
                new DateTime(1998, 01, 02, 09, 00, 00),
                new DateTime(1998, 02, 06, 09, 00, 00),
                new DateTime(1998, 03, 06, 09, 00, 00),
                new DateTime(1998, 04, 03, 09, 00, 00),
                new DateTime(1998, 05, 01, 09, 00, 00),
                new DateTime(1998, 06, 05, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_1stFridayUntil1997_12_24()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;UNTIL=19971224T000000Z;BYDAY=1FR");
            var startDate = new DateTime(1997, 09, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 05, 09, 00, 00),
                new DateTime(1997, 10, 03, 09, 00, 00),
                new DateTime(1997, 11, 07, 09, 00, 00),
                new DateTime(1997, 12, 05, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_EveryOtherMonthOnThe1stAndLastSundayOfTheMonthFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=2;COUNT=10;BYDAY=1SU,-1SU");
            var startDate = new DateTime(1997, 09, 07, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 07, 09, 00, 00),
                new DateTime(1997, 09, 28, 09, 00, 00),
                new DateTime(1997, 11, 02, 09, 00, 00),
                new DateTime(1997, 11, 30, 09, 00, 00),
                new DateTime(1998, 01, 04, 09, 00, 00),
                new DateTime(1998, 01, 25, 09, 00, 00),
                new DateTime(1998, 03, 01, 09, 00, 00),
                new DateTime(1998, 03, 29, 09, 00, 00),
                new DateTime(1998, 05, 03, 09, 00, 00),
                new DateTime(1998, 05, 31, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_OnTheSecondToLastMondayOfTheMonthFor6Months()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=6;BYDAY=-2MO");
            var startDate = new DateTime(1997, 09, 22, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 22, 09, 00, 00),
                new DateTime(1997, 10, 20, 09, 00, 00),
                new DateTime(1997, 11, 17, 09, 00, 00),
                new DateTime(1997, 12, 22, 09, 00, 00),
                new DateTime(1998, 01, 19, 09, 00, 00),
                new DateTime(1998, 02, 16, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_OnTheThirdToTheLastDayOfTheMonthForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-3");
            var startDate = new DateTime(1997, 09, 28, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 28, 09, 00, 00),
                new DateTime(1997, 10, 29, 09, 00, 00),
                new DateTime(1997, 11, 28, 09, 00, 00),
                new DateTime(1997, 12, 29, 09, 00, 00),
                new DateTime(1998, 01, 29, 09, 00, 00),
                new DateTime(1998, 02, 26, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_OnThe2ndAnd15thOfTheMonthFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=10;BYMONTHDAY=2,15");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 15, 09, 00, 00),
                new DateTime(1997, 10, 02, 09, 00, 00),
                new DateTime(1997, 10, 15, 09, 00, 00),
                new DateTime(1997, 11, 02, 09, 00, 00),
                new DateTime(1997, 11, 15, 09, 00, 00),
                new DateTime(1997, 12, 02, 09, 00, 00),
                new DateTime(1997, 12, 15, 09, 00, 00),
                new DateTime(1998, 01, 02, 09, 00, 00),
                new DateTime(1998, 01, 15, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_OnTheFirstAndLastDayOfTheMonthFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=10;BYMONTHDAY=1,-1");
            var startDate = new DateTime(1997, 09, 30, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 01, 09, 00, 00),
                new DateTime(1997, 10, 31, 09, 00, 00),
                new DateTime(1997, 11, 01, 09, 00, 00),
                new DateTime(1997, 11, 30, 09, 00, 00),
                new DateTime(1997, 12, 01, 09, 00, 00),
                new DateTime(1997, 12, 31, 09, 00, 00),
                new DateTime(1998, 01, 01, 09, 00, 00),
                new DateTime(1998, 01, 31, 09, 00, 00),
                new DateTime(1998, 02, 01, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_Every18MonthsOnThe10thThru15thOfTheMonthFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=18;COUNT=10;BYMONTHDAY=10,11,12,13,14,15");
            var startDate = new DateTime(1997, 09, 10, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 10, 09, 00, 00),
                new DateTime(1997, 09, 11, 09, 00, 00),
                new DateTime(1997, 09, 12, 09, 00, 00),
                new DateTime(1997, 09, 13, 09, 00, 00),
                new DateTime(1997, 09, 14, 09, 00, 00),
                new DateTime(1997, 09, 15, 09, 00, 00),
                new DateTime(1999, 03, 10, 09, 00, 00),
                new DateTime(1999, 03, 11, 09, 00, 00),
                new DateTime(1999, 03, 12, 09, 00, 00),
                new DateTime(1999, 03, 13, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_EveryTuesdayEveryOtherMonth()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;INTERVAL=2;BYDAY=TU");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 02, 09, 00, 00),
                new DateTime(1997, 09, 09, 09, 00, 00),
                new DateTime(1997, 09, 16, 09, 00, 00),
                new DateTime(1997, 09, 23, 09, 00, 00),
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 11, 04, 09, 00, 00),
                new DateTime(1997, 11, 11, 09, 00, 00),
                new DateTime(1997, 11, 18, 09, 00, 00),
                new DateTime(1997, 11, 25, 09, 00, 00),
                new DateTime(1998, 01, 06, 09, 00, 00),
                new DateTime(1998, 01, 13, 09, 00, 00),
                new DateTime(1998, 01, 20, 09, 00, 00),
                new DateTime(1998, 01, 27, 09, 00, 00),
                new DateTime(1998, 03, 03, 09, 00, 00),
                new DateTime(1998, 03, 10, 09, 00, 00),
                new DateTime(1998, 03, 17, 09, 00, 00),
                new DateTime(1998, 03, 24, 09, 00, 00),
                new DateTime(1998, 03, 31, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_The3rdInstanceIntoTheMonthOfOneOfTuesdayWednesdayOrThursdayForTheNext3Months()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=3;BYDAY=TU,WE,TH;BYSETPOS=3");
            var startDate = new DateTime(1997, 09, 04, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 09, 04, 09, 00, 00),
                new DateTime(1997, 10, 07, 09, 00, 00),
                new DateTime(1997, 11, 06, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_The2ndToLastWeekdayOfTheMonth()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-2");
            var startDate = new DateTime(1997, 09, 29, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 29, 09, 00, 00),
                new DateTime(1997, 10, 30, 09, 00, 00),
                new DateTime(1997, 11, 27, 09, 00, 00),
                new DateTime(1997, 12, 30, 09, 00, 00),
                new DateTime(1998, 01, 29, 09, 00, 00),
                new DateTime(1998, 02, 26, 09, 00, 00),
                new DateTime(1998, 03, 30, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_EveryFridayThe13thForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1998, 02, 13, 09, 00, 00),
                new DateTime(1998, 03, 13, 09, 00, 00),
                new DateTime(1998, 11, 13, 09, 00, 00),
                new DateTime(1999, 08, 13, 09, 00, 00),
                new DateTime(2000, 10, 13, 09, 00, 00));
        }

        [TestMethod]
        public void Monthly_TheFirstSaturdayThatFollowsTheFirstSundayOfTheMonthForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=SA;BYMONTHDAY=7,8,9,10,11,12,13");
            var startDate = new DateTime(1997, 09, 13, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 13, 09, 00, 00),
                new DateTime(1997, 10, 11, 09, 00, 00),
                new DateTime(1997, 11, 08, 09, 00, 00),
                new DateTime(1997, 12, 13, 09, 00, 00),
                new DateTime(1998, 01, 10, 09, 00, 00),
                new DateTime(1998, 02, 07, 09, 00, 00),
                new DateTime(1998, 03, 07, 09, 00, 00),
                new DateTime(1998, 04, 11, 09, 00, 00),
                new DateTime(1998, 05, 09, 09, 00, 00),
                new DateTime(1998, 06, 13, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_EverydayInJanuaryFor3Years01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;UNTIL=20000131T090000Z;BYMONTH=1;BYDAY=SU,MO,TU,WE,TH,FR,SA");
            var startDate = new DateTime(1998, 01, 01, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            var expectedDates = new List<DateTime>();
            for (var year = 0; year < 3; year++)
            {
                for (var day = 0; day < 31; day++)
                {
                    expectedDates.Add(new DateTime(1998 + year, 01, day + 1, 09, 00, 00));
                }
            }

            AssertOccurrences(occurrences, expectedDates.ToArray());
        }

        [TestMethod]
        public void Yearly_EverydayInJanuaryFor3Years02()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T090000Z;BYMONTH=1");
            var startDate = new DateTime(1998, 01, 01, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            var expectedDates = new List<DateTime>();
            for (var year = 0; year < 3; year++)
            {
                for (var day = 0; day < 31; day++)
                {
                    expectedDates.Add(new DateTime(1998 + year, 01, day + 1, 09, 00, 00));
                }
            }

            AssertOccurrences(occurrences, expectedDates.ToArray());
        }

        [TestMethod]
        public void Yearly_InJuneAndJulyFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;COUNT=10;BYMONTH=6,7");
            var startDate = new DateTime(1997, 06, 10, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 06, 10, 09, 00, 00),
                new DateTime(1997, 07, 10, 09, 00, 00),
                new DateTime(1998, 06, 10, 09, 00, 00),
                new DateTime(1998, 07, 10, 09, 00, 00),
                new DateTime(1999, 06, 10, 09, 00, 00),
                new DateTime(1999, 07, 10, 09, 00, 00),
                new DateTime(2000, 06, 10, 09, 00, 00),
                new DateTime(2000, 07, 10, 09, 00, 00),
                new DateTime(2001, 06, 10, 09, 00, 00),
                new DateTime(2001, 07, 10, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_EveryOtherYearOnJanuaryFebruaryAndMarchFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=2;COUNT=10;BYMONTH=1,2,3");
            var startDate = new DateTime(1997, 03, 10, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 03, 10, 09, 00, 00),
                new DateTime(1999, 01, 10, 09, 00, 00),
                new DateTime(1999, 02, 10, 09, 00, 00),
                new DateTime(1999, 03, 10, 09, 00, 00),
                new DateTime(2001, 01, 10, 09, 00, 00),
                new DateTime(2001, 02, 10, 09, 00, 00),
                new DateTime(2001, 03, 10, 09, 00, 00),
                new DateTime(2003, 01, 10, 09, 00, 00),
                new DateTime(2003, 02, 10, 09, 00, 00),
                new DateTime(2003, 03, 10, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_Every3rdYearOnThe1st_100thAnd200thDayFor10Occurrences()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=3;COUNT=10;BYYEARDAY=1,100,200");
            var startDate = new DateTime(1997, 01, 01, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrences(occurrences,
                new DateTime(1997, 01, 01, 09, 00, 00),
                new DateTime(1997, 04, 10, 09, 00, 00),
                new DateTime(1997, 07, 19, 09, 00, 00),
                new DateTime(2000, 01, 01, 09, 00, 00),
                new DateTime(2000, 04, 09, 09, 00, 00),
                new DateTime(2000, 07, 18, 09, 00, 00),
                new DateTime(2003, 01, 01, 09, 00, 00),
                new DateTime(2003, 04, 10, 09, 00, 00),
                new DateTime(2003, 07, 19, 09, 00, 00),
                new DateTime(2006, 01, 01, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_Every20thMondayOfTheYearForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYDAY=20MO");
            var startDate = new DateTime(1997, 01, 01, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 05, 19, 09, 00, 00),
                new DateTime(1998, 05, 18, 09, 00, 00),
                new DateTime(1999, 05, 17, 09, 00, 00));
        }

        // Currently we do not support BYWEEKNO
        //[TestMethod]
        //public void Yearly_MondayOfWeekNumber20Forever()
        //{
        //    RecurrenceRule rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=20;BYDAY=MO");
        //    DateTime startDate = new DateTime(1997, 05, 12, 09, 00, 00);
        //    var occurrences = rrule.GetNextOccurrences(startDate);

        //    AssertOccurrencesStartWith(occurrences,
        //        new DateTime(1997, 05, 12, 09, 00, 00),
        //        new DateTime(1998, 05, 11, 09, 00, 00),
        //        new DateTime(1999, 05, 17, 09, 00, 00));
        //}

        [TestMethod]
        public void Yearly_EveryThursdayInMarchForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=3;BYDAY=TH");
            var startDate = new DateTime(1997, 03, 13, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 03, 13, 09, 00, 00),
                new DateTime(1997, 03, 20, 09, 00, 00),
                new DateTime(1997, 03, 27, 09, 00, 00),
                new DateTime(1998, 03, 05, 09, 00, 00),
                new DateTime(1998, 03, 12, 09, 00, 00),
                new DateTime(1998, 03, 19, 09, 00, 00),
                new DateTime(1998, 03, 26, 09, 00, 00),
                new DateTime(1999, 03, 04, 09, 00, 00),
                new DateTime(1999, 03, 11, 09, 00, 00),
                new DateTime(1999, 03, 18, 09, 00, 00),
                new DateTime(1999, 03, 25, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_EveryThursdayButOnlyDuringJuneJulyAndAugustForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYDAY=TH;BYMONTH=6,7,8");
            var startDate = new DateTime(1997, 06, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 06, 05, 09, 00, 00),
                new DateTime(1997, 06, 12, 09, 00, 00),
                new DateTime(1997, 06, 19, 09, 00, 00),
                new DateTime(1997, 06, 26, 09, 00, 00),
                new DateTime(1997, 07, 03, 09, 00, 00),
                new DateTime(1997, 07, 10, 09, 00, 00),
                new DateTime(1997, 07, 17, 09, 00, 00),
                new DateTime(1997, 07, 24, 09, 00, 00),
                new DateTime(1997, 07, 31, 09, 00, 00),
                new DateTime(1997, 08, 07, 09, 00, 00),
                new DateTime(1997, 08, 14, 09, 00, 00),
                new DateTime(1997, 08, 21, 09, 00, 00),
                new DateTime(1997, 08, 28, 09, 00, 00),
                new DateTime(1998, 06, 04, 09, 00, 00),
                new DateTime(1998, 06, 11, 09, 00, 00),
                new DateTime(1998, 06, 18, 09, 00, 00),
                new DateTime(1998, 06, 25, 09, 00, 00),
                new DateTime(1998, 07, 02, 09, 00, 00),
                new DateTime(1998, 07, 09, 09, 00, 00),
                new DateTime(1998, 07, 16, 09, 00, 00),
                new DateTime(1998, 07, 23, 09, 00, 00),
                new DateTime(1998, 07, 30, 09, 00, 00),
                new DateTime(1998, 08, 06, 09, 00, 00),
                new DateTime(1998, 08, 13, 09, 00, 00),
                new DateTime(1998, 08, 20, 09, 00, 00),
                new DateTime(1998, 08, 27, 09, 00, 00),
                new DateTime(1999, 06, 03, 09, 00, 00),
                new DateTime(1999, 06, 10, 09, 00, 00),
                new DateTime(1999, 06, 17, 09, 00, 00),
                new DateTime(1999, 06, 24, 09, 00, 00),
                new DateTime(1999, 07, 01, 09, 00, 00),
                new DateTime(1999, 07, 08, 09, 00, 00),
                new DateTime(1999, 07, 15, 09, 00, 00),
                new DateTime(1999, 07, 22, 09, 00, 00),
                new DateTime(1999, 07, 29, 09, 00, 00),
                new DateTime(1999, 08, 05, 09, 00, 00),
                new DateTime(1999, 08, 12, 09, 00, 00),
                new DateTime(1999, 08, 19, 09, 00, 00),
                new DateTime(1999, 08, 26, 09, 00, 00));
        }

        [TestMethod]
        public void Yearly_EveryFourYearsTheFirstTuesdayAfterAMondayInNovemberForever()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=4;BYMONTH=11;BYDAY=TU;BYMONTHDAY=2,3,4,5,6,7,8");
            var startDate = new DateTime(1996, 11, 05, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1996, 11, 05, 09, 00, 00),
                new DateTime(2000, 11, 07, 09, 00, 00),
                new DateTime(2004, 11, 02, 09, 00, 00));
        }
    }
}
