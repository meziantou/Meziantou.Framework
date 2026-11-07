namespace Meziantou.Framework.Scheduling.Tests;

public partial class RecurrenceRuleTests
{
#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
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
        TestGetHumanText("FREQ=DAILY;UNTIL=20150101", "fr-FR", "tous les jours jusqu'au 1er janvier 2015");
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
        TestGetHumanText("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101", "fr-FR", "toutes les 3 semaines le mardi jusqu'au 1er janvier 2015");
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
        TestGetHumanText("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=3;UNTIL=20150101", "fr-FR", "tous les 2 mois le 3e jour jusqu'au 1er janvier 2015");
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
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=3;BYDAY=SA,SU;UNTIL=20150101", "fr-FR", "tous les mois le troisième jour de weekend jusqu'au 1er janvier 2015");
    }

    [Fact]
    public void Monthly_GetHumanText_fr_fr_10()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=4;BYDAY=SA;UNTIL=20150101", "fr-FR", "tous les mois le quatrième samedi jusqu'au 1er janvier 2015");
    }

    [Fact]
    public void Monthly_GetHumanText_fr_fr_12()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR;COUNT=10", "fr-FR", "tous les mois le dernier jour de semaine pour 10 fois");
    }

    [Fact]
    public void Yearly_GetHumanText_fr_fr_01()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1", "fr-FR", "tous les ans le 1er janvier");
    }

    [Fact]
    public void Yearly_GetHumanText_fr_fr_02()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=10;COUNT=1", "fr-FR", "tous les ans le 10 juillet pour 1 fois");
    }

    [Fact]
    public void Yearly_GetHumanText_fr_fr_03()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYDAY=SA,SU;BYSETPOS=-1;UNTIL=20150101", "fr-FR", "tous les ans le dernier jour de weekend de juillet jusqu'au 1er janvier 2015");
    }

    [Fact]
    public void Yearly_GetHumanText_fr_fr_04()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=8;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=1", "fr-FR", "tous les ans le premier jour de semaine d'août");
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

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=2TU", "tous les mois le deuxième mardi")]
    [InlineData("FREQ=MONTHLY;BYDAY=+3WE", "tous les mois le troisième mercredi")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "tous les mois le dernier vendredi")]
    [InlineData("FREQ=MONTHLY;BYDAY=-2FR", "tous les mois l'avant-dernier vendredi")]
    [InlineData("FREQ=MONTHLY;BYDAY=-5FR", "tous les mois le 5e vendredi en partant de la fin")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO,-1FR", "tous les mois le premier lundi et le dernier vendredi")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO,FR", "tous les mois le premier lundi et tous les vendredis")]
    [InlineData("FREQ=YEARLY;BYDAY=20MO", "tous les ans le 20e lundi")]
    [InlineData("FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10", "tous les ans le dernier dimanche d'octobre")]
    [InlineData("FREQ=DAILY;BYDAY=MO,WE", "tous les jours le lundi et mercredi")]
    [InlineData("FREQ=HOURLY;BYDAY=SA,SU", "toutes les heures le samedi et dimanche")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,WE", "tous les mois le lundi et mercredi")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR", "tous les mois en semaine")]
    [InlineData("FREQ=MONTHLY;BYDAY=SA,SU", "tous les mois le weekend")]
    [InlineData("FREQ=YEARLY;BYDAY=MO;BYMONTH=10", "tous les ans le lundi en octobre")]
    [InlineData("FREQ=DAILY;BYHOUR=9,17", "tous les jours aux heures 9 et 17")]
    [InlineData("FREQ=DAILY;BYHOUR=9,17;BYMINUTE=0,30", "tous les jours à 9h00, 9h30, 17h00 et 17h30")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;BYHOUR=9;BYMINUTE=5;BYSECOND=30", "toutes les semaines le lundi à 9h05m30s")]
    [InlineData("FREQ=HOURLY;BYMINUTE=0,30;BYSECOND=15", "toutes les heures aux minutes 0 et 30, à la seconde 15")]
    [InlineData("FREQ=MINUTELY;BYHOUR=8", "toutes les minutes à l'heure 8")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,6", "tous les ans en janvier et juin")]
    [InlineData("FREQ=YEARLY;BYMONTH=8", "tous les ans en août")]
    [InlineData("FREQ=MONTHLY;BYMONTH=1,6;BYMONTHDAY=1", "tous les mois le 1er jour en janvier et juin")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=100", "tous les ans le 100e jour de l'année")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1,-1", "tous les ans le 1er et le dernier jour de l'année")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-2", "tous les ans l'avant-dernier jour de l'année")]
    [InlineData("FREQ=YEARLY;BYMONTH=4,8;BYMONTHDAY=-1", "tous les ans le dernier jour d'avril et d'août")]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=1,15", "tous les ans le 1er et le 15 avril")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1", "tous les ans le 1er jour de chaque mois")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-2", "tous les mois l'avant-dernier jour")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-3", "tous les mois le 3e jour en partant de la fin")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,-1", "tous les mois le 1er et le dernier jour")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-2", "tous les mois l'avant-dernier lundi")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1,-1", "tous les mois le premier et le dernier lundi")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU;BYSETPOS=1", "tous les mois le premier lundi ou mardi")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1,-3", "tous les mois le premier lundi et le troisième lundi en partant de la fin")]
    [InlineData("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=1,-1", "tous les ans le premier et le dernier lundi de mars")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,15;BYSETPOS=-1", "tous les mois le 1er et le 15e jour, uniquement la dernière occurrence de chaque mois")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU;BYSETPOS=1,2", "toutes les semaines le lundi et mardi, uniquement la première et la deuxième occurrence de chaque semaine")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,FR;BYSETPOS=1;COUNT=3", "toutes les semaines le lundi et vendredi, uniquement la première occurrence de chaque semaine, pour 3 fois")]
    [InlineData("FREQ=DAILY;BYHOUR=9,17;BYSETPOS=1", "tous les jours aux heures 9 et 17, uniquement la première occurrence de chaque jour")]
    [InlineData("FREQ=YEARLY;BYDAY=MO;BYMONTH=3,4;BYSETPOS=1", "tous les ans le lundi en mars et avril, uniquement la première occurrence de chaque année")]
    [InlineData("FREQ=HOURLY;BYMINUTE=0,30;BYSETPOS=-1;UNTIL=20240102", "toutes les heures aux minutes 0 et 30, uniquement la dernière occurrence de chaque heure, jusqu'au 2 janvier 2024")]
    [InlineData("FREQ=MINUTELY;BYSECOND=0,30;BYSETPOS=1", "toutes les minutes aux secondes 0 et 30, uniquement la première occurrence de chaque minute")]
    [InlineData("FREQ=SECONDLY;BYHOUR=9;BYSETPOS=1", "toutes les secondes à l'heure 9, uniquement la première occurrence de chaque seconde")]
    [InlineData("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=1,15;BYSETPOS=-3", "tous les 2 mois le 1er et le 15e jour, uniquement la troisième occurrence en partant de la fin de chaque mois")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=13;BYDAY=FR", "tous les mois le vendredi 13")]
    [InlineData("FREQ=YEARLY;BYMONTH=10;BYMONTHDAY=13;BYDAY=FR", "tous les ans le vendredi 13 en octobre")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1;BYDAY=MO,TU,WE,TH,FR", "tous les mois le dernier jour si c'est un jour de semaine")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1;BYDAY=MO,SU", "tous les ans le 1er jour de l'année si c'est un lundi ou un dimanche")]
    [InlineData("FREQ=SECONDLY;INTERVAL=2", "toutes les 2 secondes")]
    [InlineData("FREQ=MINUTELY;INTERVAL=2;COUNT=1", "toutes les 2 minutes pour 1 fois")]
    [InlineData("FREQ=HOURLY;COUNT=0", "jamais")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2", "toutes les 2 semaines")]
    [InlineData("FREQ=MONTHLY;INTERVAL=12;COUNT=5", "tous les 12 mois pour 5 fois")]
    [InlineData("FREQ=YEARLY;INTERVAL=4;UNTIL=20251231", "tous les 4 ans jusqu'au 31 décembre 2025")]
    [InlineData("FREQ=HOURLY;UNTIL=20250101T103000Z", "toutes les heures jusqu'au 1er janvier 2025 à 10h30 UTC")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T000000Z", "tous les jours jusqu'au 3 janvier 2026 à 0h00 UTC")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T000000", "tous les jours jusqu'au 3 janvier 2026 à 0h00")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T103015", "tous les jours jusqu'au 3 janvier 2026 à 10h30m15s")]
    [InlineData("FREQ=DAILY;UNTIL=20260103", "tous les jours jusqu'au 3 janvier 2026")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20;BYDAY=MO", "tous les ans le lundi de la semaine 20")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20", "tous les ans chaque jour de la semaine 20")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=2,1;BYDAY=MO,TU,WE,TH,FR", "tous les ans du lundi au vendredi des semaines 1 et 2")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1,2;BYDAY=MO,WE", "tous les ans le lundi et mercredi des semaines 1 et 2")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-1;BYDAY=SA,SU", "tous les ans le weekend de la dernière semaine de l'année")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1,-1;BYDAY=MO", "tous les ans le lundi de la semaine 1 et de la dernière semaine de l'année")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-3;BYDAY=MO", "tous les ans le lundi de la troisième semaine en partant de la fin de l'année")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYMONTHDAY=1", "tous les ans le 1er jour de chaque mois si c'est dans la semaine 1")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=13;BYWEEKNO=2,20", "tous les ans le 13e jour de chaque mois si c'est dans la semaine 2 ou 20")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1;BYWEEKNO=1,-1", "tous les ans le 1er jour de chaque mois si c'est dans la semaine 1 ou la dernière semaine de l'année")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1;BYWEEKNO=1,5,-1;BYDAY=MO", "tous les ans le lundi 1er si c'est dans la semaine 1, la semaine 5 ou la dernière semaine de l'année")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYDAY=MO;WKST=SU", "tous les ans le lundi de la semaine 1, les semaines commençant le dimanche")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,2;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "tous les ans en semaine en janvier et février, uniquement la dernière occurrence de chaque année")]
    [InlineData("FREQ=MONTHLY;BYMONTH=1,2;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "tous les mois le dernier jour de semaine en janvier et février")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;BYHOUR=9,17;BYMINUTE=0", "tous les mois le dernier jour de semaine à 17h00")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1;BYHOUR=9,17", "tous les mois le premier lundi à l'heure 9")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=2,4;BYHOUR=9,17;BYMINUTE=0", "tous les mois le premier et le deuxième lundi à 17h00")]
    [InlineData("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=-2;BYHOUR=9,17;BYMINUTE=0", "tous les ans le dernier lundi de mars à 9h00")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1,-1;BYHOUR=9,17;BYMINUTE=0", "tous les mois le lundi à 9h00 et 17h00, uniquement la première et la dernière occurrence de chaque mois")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1;BYDAY=1MO", "tous les ans le 1er jour de chaque mois si c'est le premier lundi de l'année")]
    [InlineData("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1;BYDAY=1MO", "tous les ans le 1er janvier si c'est le premier lundi")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1;BYDAY=1MO", "tous les mois le 1er jour si c'est le premier lundi")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1,32;BYMONTHDAY=1", "tous les ans le 1er et le 32e jour de l'année si c'est le 1er jour du mois")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1;BYMONTHDAY=1,-1;BYDAY=MO", "tous les ans le 1er jour de l'année si c'est le 1er ou le dernier jour du mois et un lundi")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-1;BYDAY=-1SU", "tous les ans le dernier jour de l'année si c'est le dernier dimanche de l'année")]
    [InlineData("FREQ=HOURLY;BYYEARDAY=1", "toutes les heures le 1er jour de l'année")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU,MO;WKST=SU", "toutes les 2 semaines le dimanche et lundi, les semaines commençant le dimanche")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU,MO;WKST=MO", "toutes les 2 semaines le dimanche et lundi")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;WKST=SU", "toutes les 2 semaines le lundi et mercredi, les semaines commençant le dimanche")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;WKST=SU", "toutes les 2 semaines le lundi, les semaines commençant le dimanche")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU;WKST=SU", "toutes les 2 semaines le dimanche, les semaines commençant le dimanche")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH;WKST=TU", "toutes les 2 semaines le mardi et jeudi, les semaines commençant le mardi")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;WKST=SU", "toutes les 2 semaines")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU;WKST=TU;BYSETPOS=1", "toutes les semaines le lundi et mardi, uniquement la première occurrence de chaque semaine, les semaines commençant le mardi")]
    [InlineData("FREQ=WEEKLY;BYDAY=SU,MO;WKST=SU;COUNT=3", "toutes les semaines le dimanche et lundi pour 3 fois")]
    [InlineData("FREQ=DAILY;BYHOUR=17,9;BYMINUTE=30,0", "tous les jours à 9h00, 9h30, 17h00 et 17h30")]
    [InlineData("FREQ=DAILY;BYHOUR=9,9,17", "tous les jours aux heures 9 et 17")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1,15,1,15", "tous les mois le 1er, le 15e et le dernier jour")]
    [InlineData("FREQ=YEARLY;BYMONTH=6,1", "tous les ans en janvier et juin")]
    public void GetHumanText_fr_fr(string rrule, string expected)
    {
        TestGetHumanText(rrule, "fr-FR", expected);
    }
#endif

    // The culture is resolved from its name, so invariant globalization mode, which has no culture data, resolves it identically
    [Theory]
    [InlineData("", "every day")]
    [InlineData("en", "every day")]
    [InlineData("en-US", "every day")]
    [InlineData("en-GB", "every day")]
    [InlineData("fr", "tous les jours")]
    [InlineData("fr-FR", "tous les jours")]
    [InlineData("fr-CA", "tous les jours")]
    [InlineData("fr-CH", "tous les jours")]
    [InlineData("de", null)]
    [InlineData("de-DE", null)]
    [InlineData("zh-Hans-CN", null)]
    public void GetHumanText_CultureResolvedByLanguage(string cultureName, string? expected)
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Equal(expected, rrule.GetHumanText(CultureInfo.GetCultureInfo(cultureName)));
    }
}
