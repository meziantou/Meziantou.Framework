using Xunit;

namespace Meziantou.Framework.Scheduling.Tests
{
    public partial class RecurrenceRuleTests
    {
#if !InvariantGlobalization
        [Fact]
        public void Daily_HumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=DAILY", "fr-FR", "tous les jours");
        }

        [Fact]
        public void Daily_HumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=1", "fr-FR", "tous les jours");
        }

        [Fact]
        public void Daily_HumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=2", "fr-FR", "tous les 2 jours");
        }

        [Fact]
        public void Daily_HumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=DAILY;INTERVAL=3", "fr-FR", "tous les 3 jours");
        }

        [Fact]
        public void Daily_HumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=DAILY;COUNT=10", "fr-FR", "tous les jours pour 10 fois");
        }

        [Fact]
        public void Daily_HumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=DAILY;UNTIL=20150101", "fr-FR", "tous les jours jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=1", "fr-FR", "toutes les semaines");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,FR", "fr-FR", "toutes les semaines le lundi, mardi, mercredi et vendredi");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;COUNT=3", "fr-FR", "toutes les 2 semaines le lundi pour 3 fois");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101", "fr-FR", "toutes les 3 semaines le mardi jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "fr-FR", "toutes les semaines le lundi, mardi, mercredi, jeudi et vendredi");
        }

        [Fact]
        public void Weekly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=WEEKLY;BYDAY=SA,SU", "fr-FR", "toutes les semaines le samedi et dimanche");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=1", "fr-FR", "tous les mois le 1er jour");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=2;COUNT=4", "fr-FR", "tous les mois le 2e jour pour 4 fois");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=3;UNTIL=20150101", "fr-FR", "tous les 2 mois le 3e jour jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=10", "fr-FR", "tous les 3 mois le 10e jour");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=-1", "fr-FR", "tous les mois le dernier jour");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO", "fr-FR", "tous les mois le premier lundi");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_07()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "fr-FR", "tous les mois le premier jour de semaine pour 7 fois");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_08()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=2;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "fr-FR", "tous les mois le deuxième jour de semaine pour 7 fois");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_09()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=3;BYDAY=SA,SU;UNTIL=20150101", "fr-FR", "tous les mois le troisième jour de weekend jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_10()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=4;BYDAY=SA;UNTIL=20150101", "fr-FR", "tous les mois le quatrième samedi jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Monthly_GetHumanText_fr_fr_12()
        {
            TestGetHumanText("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR;COUNT=10", "fr-FR", "tous les mois le dernier jour de semaine pour 10 fois");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_01()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1", "fr-FR", "tous les ans le 1 janvier");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_02()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=10;COUNT=1", "fr-FR", "tous les ans le 10 juillet pour 1 fois");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_03()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYDAY=SA,SU;BYSETPOS=-1;UNTIL=20150101", "fr-FR", "tous les ans le dernier jour de weekend de juillet jusqu'au 1 janvier 2015");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_04()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=8;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=1", "fr-FR", "tous les ans le premier jour de semaine d'aout");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_05()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=6;BYDAY=WE;BYSETPOS=2", "fr-FR", "tous les ans le deuxième mercredi de juin");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_06()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1;INTERVAL=3", "fr-FR", "tous les 3 ans le dernier jour de février");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_07()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=3", "fr-FR", "tous les ans le troisième lundi de mars");
        }

        [Fact]
        public void Yearly_GetHumanText_fr_fr_08()
        {
            TestGetHumanText("FREQ=YEARLY;BYMONTH=5;BYDAY=TH;BYSETPOS=4;INTERVAL=2", "fr-FR", "tous les 2 ans le quatrième jeudi de mai");
        }
#endif
    }
}
