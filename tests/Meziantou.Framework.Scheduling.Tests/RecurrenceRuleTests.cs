using System;
using System.Globalization;
using Meziantou.Framework.Scheduling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Scheduling.Tests
{
    public partial class RecurrenceRuleTests
    {
        [TestMethod]
        public void Monthly_TheLastDayOfTheMonth()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1");
            var startDate = new DateTime(1997, 09, 02, 09, 00, 00);
            var occurrences = rrule.GetNextOccurrences(startDate);

            AssertOccurrencesStartWith(occurrences,
                new DateTime(1997, 09, 30, 09, 00, 00),
                new DateTime(1997, 10, 31, 09, 00, 00),
                new DateTime(1997, 11, 30, 09, 00, 00));
        }

        [TestMethod]
        public void Daily_Text01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");

            var text = rrule.Text;

            Assert.AreEqual("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1", text);
        }

        [TestMethod]
        public void Daily_Text02()
        {
            var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;INTERVAL=2");

            var text = rrule.Text;

            Assert.AreEqual("FREQ=DAILY;INTERVAL=2;UNTIL=20000131T140000Z", text);
        }

        [TestMethod]
        public void Weekly_Text01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE");

            var text = rrule.Text;

            Assert.AreEqual("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE", text);
        }

        [TestMethod]
        public void Monthly_Text01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

            var text = rrule.Text;

            Assert.AreEqual("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYMONTHDAY=2;BYDAY=TU,WE", text);
        }

        [TestMethod]
        public void Yearly_Text01()
        {
            var rrule = RecurrenceRule.Parse("FREQ=YEARLY;UNTIL=20000131T140000Z;BYYEARDAY=1,-1;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

            var text = rrule.Text;

            Assert.AreEqual("FREQ=YEARLY;UNTIL=20000131T140000Z;BYMONTH=1;BYYEARDAY=1,-1;BYMONTHDAY=2;BYDAY=TU,WE", text);
        }

        private static void TestGetHumanText(string rruleText, string cultureInfo, string expectedText)
        {
            var rrule = RecurrenceRule.Parse(rruleText);
            var text = rrule.GetHumanText(new CultureInfo(cultureInfo));
            Assert.AreEqual(expectedText, text);
        }

        [TestMethod]
        public void Daily_HumanText_en_us_01()
        {
            TestGetHumanText("FREQ=DAILY", "en-US", "every day");
        }

        [TestMethod]
        public void Daily_HumanText_en_us_02()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=1", "en-US", "every day");
        }

        [TestMethod]
        public void Daily_HumanText_en_us_03()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=2", "en-US", "every other day");
        }

        [TestMethod]
        public void Daily_HumanText_en_us_04()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=3", "en-US", "every 3 days");
        }

        [TestMethod]
        public void Daily_HumanText_en_us_05()
        {
            TestGetHumanText("FREQ=DAILY;COUNT=10", "en-US", "every day for 10 times");
        }

        [TestMethod]
        public void Daily_HumanText_en_us_06()
        {
            TestGetHumanText("FREQ=DAILY;UNTIL=20150101", "en-US", "every day until January 1, 2015");
        }


        [TestMethod]
        public void Weekly_GetHumanText_en_us_01()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=1", "en-US", "every week");
        }

        [TestMethod]
        public void Weekly_GetHumanText_en_us_02()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,FR", "en-US", "every week on Monday, Tuesday, Wednesday and Friday");
        }

        [TestMethod]
        public void Weekly_GetHumanText_en_us_03()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;COUNT=3", "en-US", "every other week on Monday for 3 times");
        }

        [TestMethod]
        public void Weekly_GetHumanText_en_us_04()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101", "en-US", "every 3 weeks on Tuesday until January 1, 2015");
        }

        [TestMethod]
        public void Weekly_GetHumanText_en_us_05()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "en-US", "every week on Monday, Tuesday, Wednesday, Thursday and Friday");
        }

        [TestMethod]
        public void Weekly_GetHumanText_en_us_06()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=SA,SU", "en-US", "every week on Saturday and Sunday");
        }


        [TestMethod]
        public void Monthly_GetHumanText_en_us_01()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=1", "en-US", "every month the 1st");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_02()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=2;COUNT=4", "en-US", "every month the 2nd for 4 times");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_03()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=3;UNTIL=20150101", "en-US", "every other month the 3rd until January 1, 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_04()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=10", "en-US", "every 3 months the 10th");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_05()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=-1", "en-US", "every month on the last day");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_06()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO", "en-US", "every month on the first Monday");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_07()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "en-US", "every month on the first weekday for 7 times");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_08()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=2;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "en-US", "every month on the second weekday for 7 times");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_09()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=3;BYDAY=SA,SU;UNTIL=20150101", "en-US", "every month on the third weekend day until January 1, 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_10()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=4;BYDAY=SA;UNTIL=20150101", "en-US", "every month on the fourth Saturday until January 1, 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_en_us_12()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR;COUNT=10", "en-US", "every month on the last weekday for 10 times");
        }


        [TestMethod]
        public void Yearly_GetHumanText_en_us_01()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1", "en-US", "every year on January the 1st");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_02()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=10;COUNT=1", "en-US", "every year on July the 10th for 1 time");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_03()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYDAY=SA,SU;BYSETPOS=-1;UNTIL=20150101", "en-US", "every year on the last weekend day of July until January 1, 2015");
        }


        [TestMethod]
        public void Yearly_GetHumanText_en_us_04()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=8;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=1", "en-US", "every year on the first weekday of August");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_05()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=6;BYDAY=WE;BYSETPOS=2", "en-US", "every year on the second Wednesday of June");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_06()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1;INTERVAL=3", "en-US", "every 3 years on the last day of February");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_07()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=3", "en-US", "every year on the third Monday of March");
        }

        [TestMethod]
        public void Yearly_GetHumanText_en_us_08()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=5;BYDAY=TH;BYSETPOS=4;INTERVAL=2", "en-US", "every other year on the fourth Thursday of May");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=DAILY", "fr-FR", "tous les jours");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=1", "fr-FR", "tous les jours");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=2", "fr-FR", "tous les 2 jours");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=3", "fr-FR", "tous les 3 jours");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=DAILY;COUNT=10", "fr-FR", "tous les jours pour 10 fois");
        }

        [TestMethod]
        public void Daily_HumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=DAILY;UNTIL=20150101", "fr-FR", "tous les jours jusqu'au 1 janvier 2015");
        }


        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=1", "fr-FR", "toutes les semaines");
        }

        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,FR", "fr-FR", "toutes les semaines le lundi, mardi, mercredi et vendredi");
        }

        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;COUNT=3", "fr-FR", "toutes les 2 semaines le lundi pour 3 fois");
        }

        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101", "fr-FR", "toutes les 3 semaines le mardi jusqu'au 1 janvier 2015");
        }

        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "fr-FR", "toutes les semaines le lundi, mardi, mercredi, jeudi et vendredi");
        }

        [TestMethod]
        public void Weekly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=SA,SU", "fr-FR", "toutes les semaines le samedi et dimanche");
        }


        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=1", "fr-FR", "tous les mois le 1er jour");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=2;COUNT=4", "fr-FR", "tous les mois le 2e jour pour 4 fois");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=3;UNTIL=20150101", "fr-FR", "tous les 2 mois le 3e jour jusqu'au 1 janvier 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=10", "fr-FR", "tous les 3 mois le 10e jour");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=-1", "fr-FR", "tous les mois le dernier jour");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO", "fr-FR", "tous les mois le premier lundi");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_07()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "fr-FR", "tous les mois le premier jour de semaine pour 7 fois");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_08()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=2;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "fr-FR", "tous les mois le deuxième jour de semaine pour 7 fois");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_09()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=3;BYDAY=SA,SU;UNTIL=20150101", "fr-FR", "tous les mois le troisième jour de weekend jusqu'au 1 janvier 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_10()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=4;BYDAY=SA;UNTIL=20150101", "fr-FR", "tous les mois le quatrième samedi jusqu'au 1 janvier 2015");
        }

        [TestMethod]
        public void Monthly_GetHumanText_fr_fr_12()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR;COUNT=10", "fr-FR", "tous les mois le dernier jour de semaine pour 10 fois");
        }


        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1", "fr-FR", "tous les ans le 1 janvier");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=10;COUNT=1", "fr-FR", "tous les ans le 10 juillet pour 1 fois");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYDAY=SA,SU;BYSETPOS=-1;UNTIL=20150101", "fr-FR", "tous les ans le dernier jour de weekend de juillet jusqu'au 1 janvier 2015");
        }


        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=8;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=1", "fr-FR", "tous les ans le premier jour de semaine d'aout");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=6;BYDAY=WE;BYSETPOS=2", "fr-FR", "tous les ans le deuxième mercredi de juin");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1;INTERVAL=3", "fr-FR", "tous les 3 ans le dernier jour de février");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_07()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=3", "fr-FR", "tous les ans le troisième lundi de mars");
        }

        [TestMethod]
        public void Yearly_GetHumanText_fr_fr_08()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=5;BYDAY=TH;BYSETPOS=4;INTERVAL=2", "fr-FR", "tous les 2 ans le quatrième jeudi de mai");
        }

    }
}
