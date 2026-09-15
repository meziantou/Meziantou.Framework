namespace Meziantou.Framework.Scheduling.Tests;

public partial class RecurrenceRuleTests
{
    [Fact]
    public void Daily_HumanText_en_us_01()
    {
        TestGetHumanText("FREQ=DAILY", "en-US", "every day");
    }

    [Fact]
    public void Daily_HumanText_en_us_02()
    {
        TestGetHumanText("FREQ=DAILY;INTERVAL=1", "en-US", "every day");
    }

    [Fact]
    public void Daily_HumanText_en_us_03()
    {
        TestGetHumanText("FREQ=DAILY;INTERVAL=2", "en-US", "every other day");
    }

    [Fact]
    public void Daily_HumanText_en_us_04()
    {
        TestGetHumanText("FREQ=DAILY;INTERVAL=3", "en-US", "every 3 days");
    }

    [Fact]
    public void Daily_HumanText_en_us_05()
    {
        TestGetHumanText("FREQ=DAILY;COUNT=10", "en-US", "every day for 10 times");
    }

    [Fact]
    public void Daily_HumanText_en_us_06()
    {
        TestGetHumanText("FREQ=DAILY;UNTIL=20150101", "en-US", "every day until January 1, 2015");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_01()
    {
        TestGetHumanText("FREQ=WEEKLY;INTERVAL=1", "en-US", "every week");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_02()
    {
        TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,FR", "en-US", "every week on Monday, Tuesday, Wednesday and Friday");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_03()
    {
        TestGetHumanText("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;COUNT=3", "en-US", "every other week on Monday for 3 times");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_04()
    {
        TestGetHumanText("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101", "en-US", "every 3 weeks on Tuesday until January 1, 2015");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_05()
    {
        TestGetHumanText("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "en-US", "every week on Monday, Tuesday, Wednesday, Thursday and Friday");
    }

    [Fact]
    public void Weekly_GetHumanText_en_us_06()
    {
        TestGetHumanText("FREQ=WEEKLY;BYDAY=SA,SU", "en-US", "every week on Saturday and Sunday");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_01()
    {
        TestGetHumanText("FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=1", "en-US", "every month on the 1st");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_02()
    {
        TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=2;COUNT=4", "en-US", "every month on the 2nd for 4 times");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_03()
    {
        TestGetHumanText("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=3;UNTIL=20150101", "en-US", "every other month on the 3rd until January 1, 2015");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_04()
    {
        TestGetHumanText("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=10", "en-US", "every 3 months on the 10th");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_05()
    {
        TestGetHumanText("FREQ=MONTHLY;BYMONTHDAY=-1", "en-US", "every month on the last day");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_06()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO", "en-US", "every month on the first Monday");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_07()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=1;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "en-US", "every month on the first weekday for 7 times");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_08()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=2;BYDAY=MO,TU,WE,TH,FR;COUNT=7", "en-US", "every month on the second weekday for 7 times");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_09()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=3;BYDAY=SA,SU;UNTIL=20150101", "en-US", "every month on the third weekend day until January 1, 2015");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_10()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=4;BYDAY=SA;UNTIL=20150101", "en-US", "every month on the fourth Saturday until January 1, 2015");
    }

    [Fact]
    public void Monthly_GetHumanText_en_us_12()
    {
        TestGetHumanText("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR;COUNT=10", "en-US", "every month on the last weekday for 10 times");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_01()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1", "en-US", "every year on January the 1st");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_02()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=10;COUNT=1", "en-US", "every year on July the 10th for 1 time");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_03()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=7;BYDAY=SA,SU;BYSETPOS=-1;UNTIL=20150101", "en-US", "every year on the last weekend day of July until January 1, 2015");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_04()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=8;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=1", "en-US", "every year on the first weekday of August");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_05()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=6;BYDAY=WE;BYSETPOS=2", "en-US", "every year on the second Wednesday of June");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_06()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1;INTERVAL=3", "en-US", "every 3 years on the last day of February");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_07()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=3", "en-US", "every year on the third Monday of March");
    }

    [Fact]
    public void Yearly_GetHumanText_en_us_08()
    {
        TestGetHumanText("FREQ=YEARLY;BYMONTH=5;BYDAY=TH;BYSETPOS=4;INTERVAL=2", "en-US", "every other year on the fourth Thursday of May");
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=2TU", "every month on the second Tuesday")]
    [InlineData("FREQ=MONTHLY;BYDAY=+3WE", "every month on the third Wednesday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "every month on the last Friday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-2FR", "every month on the second to last Friday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-5FR", "every month on the 5th to last Friday")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO,-1FR", "every month on the first Monday and the last Friday")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO,FR", "every month on the first Monday and every Friday")]
    [InlineData("FREQ=YEARLY;BYDAY=20MO", "every year on the 20th Monday")]
    [InlineData("FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10", "every year on the last Sunday of October")]
    [InlineData("FREQ=DAILY;BYDAY=MO,WE", "every day on Monday and Wednesday")]
    [InlineData("FREQ=HOURLY;BYDAY=SA,SU", "every hour on Saturday and Sunday")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,WE", "every month on Mondays and Wednesdays")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR", "every month on weekdays")]
    [InlineData("FREQ=YEARLY;BYDAY=MO;BYMONTH=10", "every year on Mondays in October")]
    [InlineData("FREQ=DAILY;BYHOUR=9,17", "every day at hours 9 and 17")]
    [InlineData("FREQ=DAILY;BYHOUR=9,17;BYMINUTE=0,30", "every day at 9:00, 9:30, 17:00 and 17:30")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;BYHOUR=9;BYMINUTE=5;BYSECOND=30", "every week on Monday at 9:05:30")]
    [InlineData("FREQ=HOURLY;BYMINUTE=0,30;BYSECOND=15", "every hour at minutes 0 and 30, second 15")]
    [InlineData("FREQ=MINUTELY;BYSECOND=0,15,30,45", "every minute at seconds 0, 15, 30 and 45")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,6", "every year in January and June")]
    [InlineData("FREQ=DAILY;BYMONTH=12", "every day in December")]
    [InlineData("FREQ=MONTHLY;BYMONTH=1,6;BYMONTHDAY=1", "every month on the 1st in January and June")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=100", "every year on the 100th day of the year")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1,-1", "every year on the 1st and last day of the year")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-2", "every year on the second to last day of the year")]
    [InlineData("FREQ=YEARLY;BYMONTH=4,8;BYMONTHDAY=-1", "every year on the last day of April and August")]
    [InlineData("FREQ=YEARLY;BYMONTH=4,8;BYMONTHDAY=1,15", "every year on the 1st and 15th of April and August")]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=1,15", "every year on April the 1st and 15th")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1", "every year on the 1st of every month")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-2", "every month on the second to last day")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,-1", "every month on the 1st and last day")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=11,12,13,21,22,23", "every month on the 11th, 12th, 13th, 21st, 22nd and 23rd")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-2", "every month on the second to last Monday")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1,-1", "every month on the first and last Monday")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU;BYSETPOS=1", "every month on the first Monday or Tuesday")]
    [InlineData("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=1,-1", "every year on the first and last Monday of March")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,15;BYSETPOS=-1", "every month on the 1st and 15th, only the last occurrence")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU;BYSETPOS=1,2", "every week on Monday and Tuesday, only the first and second occurrences")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=13;BYDAY=FR", "every month on Friday the 13th")]
    [InlineData("FREQ=YEARLY;BYMONTH=10;BYMONTHDAY=13;BYDAY=FR", "every year on Friday the 13th in October")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1;BYDAY=MO,TU,WE,TH,FR", "every month on the last day if it is a weekday")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1;BYDAY=MO,SU", "every year on the 1st day of the year if it is a Monday or a Sunday")]
    [InlineData("FREQ=SECONDLY;INTERVAL=2", "every other second")]
    [InlineData("FREQ=MINUTELY;INTERVAL=2;COUNT=1", "every other minute for 1 time")]
    [InlineData("FREQ=HOURLY;COUNT=0", "every hour for 0 times")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2", "every other week")]
    [InlineData("FREQ=MONTHLY;INTERVAL=12;COUNT=5", "every 12 months for 5 times")]
    [InlineData("FREQ=YEARLY;INTERVAL=4;UNTIL=20251231", "every 4 years until December 31, 2025")]
    [InlineData("FREQ=HOURLY;UNTIL=20250101T103000Z", "every hour until January 1, 2025 at 10:30 UTC")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T000000Z", "every day until January 3, 2026 at 0:00 UTC")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T000000", "every day until January 3, 2026 at 0:00")]
    [InlineData("FREQ=DAILY;UNTIL=20260103T103015", "every day until January 3, 2026 at 10:30:15")]
    [InlineData("FREQ=DAILY;UNTIL=20260103", "every day until January 3, 2026")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20;BYDAY=MO", "every year on Monday of week 20")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20", "every year on every day of week 20")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=2,1;BYDAY=MO,TU,WE,TH,FR", "every year on weekdays of weeks 1 and 2")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1,2;BYDAY=MO,WE", "every year on Mondays and Wednesdays of weeks 1 and 2")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-1;BYDAY=SA,SU", "every year on weekend days of the last week of the year")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1,-1;BYDAY=MO", "every year on Mondays of week 1 and the last week of the year")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYMONTHDAY=1", "every year on the 1st of every month if it is in week 1")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYDAY=MO;WKST=SU", "every year on Monday of week 1, with weeks starting on Sunday")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,2;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "every year on weekdays in January and February, only the last occurrence")]
    [InlineData("FREQ=MONTHLY;BYMONTH=1,2;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "every month on the last weekday in January and February")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;BYHOUR=9,17;BYMINUTE=0", "every month on the last weekday at 17:00")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1;BYHOUR=9,17", "every month on the first Monday at hour 9")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=2,4;BYHOUR=9,17;BYMINUTE=0", "every month on the first and second Monday at 17:00")]
    [InlineData("FREQ=YEARLY;BYMONTH=3;BYDAY=MO;BYSETPOS=-2;BYHOUR=9,17;BYMINUTE=0", "every year on the last Monday of March at 9:00")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=1,-1;BYHOUR=9,17;BYMINUTE=0", "every month on Mondays at 9:00 and 17:00, only the first and last occurrences")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=1;BYDAY=1MO", "every year on the 1st of every month if it is the first Monday of the year")]
    [InlineData("FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=1;BYDAY=1MO", "every year on January the 1st if it is the first Monday")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1;BYDAY=1MO", "every month on the 1st if it is the first Monday")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1,32;BYMONTHDAY=1", "every year on the 1st and 32nd day of the year if it is the 1st of the month")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1;BYMONTHDAY=1,-1;BYDAY=MO", "every year on the 1st day of the year if it is the 1st or last day of the month and a Monday")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-1;BYDAY=-1SU", "every year on the last day of the year if it is the last Sunday of the year")]
    [InlineData("FREQ=HOURLY;BYYEARDAY=1", "every hour on the 1st day of the year")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU,MO;WKST=SU", "every other week on Sunday and Monday, with weeks starting on Sunday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU,MO;WKST=MO", "every other week on Sunday and Monday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;WKST=SU", "every other week on Monday and Wednesday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;WKST=SU", "every other week on Monday")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU;WKST=TU;BYSETPOS=1", "every week on Monday and Tuesday, only the first occurrence, with weeks starting on Tuesday")]
    [InlineData("FREQ=WEEKLY;BYDAY=SU,MO;WKST=SU;COUNT=3", "every week on Sunday and Monday for 3 times")]
    [InlineData("FREQ=DAILY;BYHOUR=17,9;BYMINUTE=30,0", "every day at 9:00, 9:30, 17:00 and 17:30")]
    [InlineData("FREQ=DAILY;BYHOUR=9,9,17", "every day at hours 9 and 17")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1,15,1,15", "every month on the 1st, 15th and last day")]
    [InlineData("FREQ=YEARLY;BYMONTH=6,1", "every year in January and June")]
    public void GetHumanText_en_us(string rrule, string expected)
    {
        TestGetHumanText(rrule, "en-US", expected);
    }

    [Fact]
    public void GetHumanText_en_us_EndDateSetProgrammatically()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");
        rrule.EndDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("every day until January 3, 2026 at 0:00 UTC", rrule.GetHumanText(CultureInfo.InvariantCulture));
    }
}
