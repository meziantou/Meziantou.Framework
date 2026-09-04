using Meziantou.Xunit;

namespace Meziantou.Framework.Scheduling.Tests;

public partial class RecurrenceRuleTests
{
    [Fact]
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

    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1")]
    public void IsForever_RuleWithoutEndCondition(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);

        Assert.True(rrule.IsForever);
    }

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=5")]
    [InlineData("FREQ=DAILY;UNTIL=20000131T140000Z")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;COUNT=1")]
    public void IsForever_RuleWithEndCondition(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);

        Assert.False(rrule.IsForever);
    }

    [Fact]
    public void Daily_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");

        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1", text);
    }

    [Fact]
    public void Daily_Text02()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;INTERVAL=2");

        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;INTERVAL=2;UNTIL=20000131T140000Z", text);
    }

    [Fact]
    public void GetNextOccurrence_ReturnsTheFirstOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        Assert.Equal(startDate, rrule.GetNextOccurrence(startDate));
    }

    [Fact]
    public void GetNextOccurrence_ReturnsNullWhenTheRuleIsExhausted()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=0");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        Assert.Null(rrule.GetNextOccurrence(startDate));
    }

    [Fact]
    public void GetNextOccurrence_ReturnsNullWhenTheEndDateHasPassed()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20230101T000000Z");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        Assert.Null(rrule.GetNextOccurrence(startDate));
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_ReturnsTheFirstOccurrenceWithTheSameOffset()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        var startDate = new DateTimeOffset(2024, 01, 01, 09, 00, 00, TimeSpan.FromHours(2));

        var occurrence = rrule.GetNextOccurrence(startDate);

        Assert.NotNull(occurrence);
        Assert.Equal(startDate, occurrence);
        Assert.Equal(TimeSpan.FromHours(2), occurrence.Value.Offset);
        Assert.Equal(new DateTime(2024, 01, 01, 09, 00, 00), occurrence.Value.DateTime);
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_UsesTheLocalTimeOfTheStartDate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=10");
        var startDate = new DateTimeOffset(2024, 01, 01, 09, 00, 00, TimeSpan.FromHours(-5));

        var occurrence = rrule.GetNextOccurrence(startDate);

        Assert.NotNull(occurrence);
        Assert.Equal(new DateTimeOffset(2024, 01, 01, 10, 00, 00, TimeSpan.FromHours(-5)), occurrence);
        Assert.Equal(TimeSpan.FromHours(-5), occurrence.Value.Offset);
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_ReturnsNullWhenTheRuleIsExhausted()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=0");
        var startDate = new DateTimeOffset(2024, 01, 01, 09, 00, 00, TimeSpan.FromHours(2));

        Assert.Null(rrule.GetNextOccurrence(startDate));
    }

    [Fact]
    public void Weekly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE");

        var text = rrule.Text;
        Assert.Equal("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE", text);
    }

    [Fact]
    public void Monthly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

        var text = rrule.Text;
        Assert.Equal("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYMONTHDAY=2;BYDAY=TU,WE", text);
    }

    [Fact]
    public void Monthly_ByMonthKeepsOnlyTheListedMonths()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTH=1;BYMONTHDAY=15");
        var startDate = new DateTime(2024, 01, 01, 00, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 01, 15, 00, 00, 00),
            new DateTime(2025, 01, 15, 00, 00, 00),
            new DateTime(2026, 01, 15, 00, 00, 00));
    }

    [Fact]
    public void Monthly_ByMonthWithSeveralMonths()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTH=3,6;BYMONTHDAY=1");
        var startDate = new DateTime(2024, 01, 01, 00, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 03, 01, 00, 00, 00),
            new DateTime(2024, 06, 01, 00, 00, 00),
            new DateTime(2025, 03, 01, 00, 00, 00),
            new DateTime(2025, 06, 01, 00, 00, 00));
    }

    [Fact]
    public void Yearly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;UNTIL=20000131T140000Z;BYYEARDAY=1,-1;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

        var text = rrule.Text;
        Assert.Equal("FREQ=YEARLY;UNTIL=20000131T140000Z;BYMONTH=1;BYYEARDAY=1,-1;BYMONTHDAY=2;BYDAY=TU,WE", text);
    }

    [Fact]
    public void BySecond_Daily_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYSECOND=0,15,30,45");
        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;BYSECOND=0,15,30,45", text);
    }

    [Fact]
    public void BySecond_Daily_ExpandsToMultipleOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=8;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 2, 9, 0, 0),
            new DateTime(2020, 1, 2, 9, 0, 30),
            new DateTime(2020, 1, 3, 9, 0, 0),
            new DateTime(2020, 1, 3, 9, 0, 30),
            new DateTime(2020, 1, 4, 9, 0, 0),
            new DateTime(2020, 1, 4, 9, 0, 30));
    }

    [Fact]
    public void BySecond_InvalidValue_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYSECOND=61"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYSECOND=-1"));
    }

    [Fact]
    public void BySecond_LeapSecond_IsNormalizedToTheLastSecondOfTheMinute()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYSECOND=60");
        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;BYSECOND=59", text);
    }

    [Fact]
    public void BySecond_LeapSecond_IsDeduplicatedAgainstTheLastSecond()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYSECOND=59,60");
        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;BYSECOND=59", text);
    }

    [Fact]
    public void Minutely_LeapSecond_ProducesOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;COUNT=3;BYSECOND=60");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2024, 01, 01, 09, 00, 59),
            new DateTime(2024, 01, 01, 09, 01, 59),
            new DateTime(2024, 01, 01, 09, 02, 59));
    }

    [Fact]
    public void Daily_LeapSecond_ProducesOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=2;BYSECOND=60");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2024, 01, 01, 09, 00, 59),
            new DateTime(2024, 01, 02, 09, 00, 59));
    }

    [Fact]
    public void BySecond_Weekly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4;BYDAY=MO;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 6, 9, 0, 0); // Monday
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 6, 9, 0, 0),
            new DateTime(2020, 1, 6, 9, 0, 30),
            new DateTime(2020, 1, 13, 9, 0, 0),
            new DateTime(2020, 1, 13, 9, 0, 30));
    }

    [Fact]
    public void BySecond_Monthly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=4;BYMONTHDAY=1;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 2, 1, 9, 0, 0),
            new DateTime(2020, 2, 1, 9, 0, 30));
    }

    [Fact]
    public void BySecond_Yearly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;COUNT=4;BYMONTH=1;BYMONTHDAY=1;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2021, 1, 1, 9, 0, 0),
            new DateTime(2021, 1, 1, 9, 0, 30));
    }

    [Theory]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;INTERVAL=-1")]
    [InlineData("FREQ=WEEKLY;INTERVAL=0;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;INTERVAL=-2")]
    public void Parse_RejectsAnIntervalBelowOne(string rruleText)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("INTERVAL", error);
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rruleText));
    }

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=-1")]
    [InlineData("FREQ=DAILY;COUNT=-5")]
    public void Parse_RejectsANegativeCount(string rruleText)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("COUNT", error);
    }

    [Theory]
    [InlineData("FREQ=DAILY;INTERVAL=1")]
    [InlineData("FREQ=DAILY;INTERVAL=2")]
    [InlineData("FREQ=DAILY;COUNT=0")]
    [InlineData("FREQ=DAILY;COUNT=1")]
    public void Parse_AcceptsValidIntervalAndCount(string rruleText)
    {
        Assert.True(RecurrenceRule.TryParse(rruleText, out _, out _));
    }

    [Fact]
    public void Interval_SetterRejectsValuesBelowOne()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Throws<ArgumentOutOfRangeException>(() => rrule.Interval = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => rrule.Interval = -1);
        Assert.Equal(1, rrule.Interval);
    }

    [Fact]
    public void Occurrences_SetterRejectsNegativeValues()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Throws<ArgumentOutOfRangeException>(() => rrule.Occurrences = -1);

        rrule.Occurrences = null;
        Assert.Null(rrule.Occurrences);
    }

    [Fact]
    public void ByMinute_Daily_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYMINUTE=0,15,30,45");
        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;BYMINUTE=0,15,30,45", text);
    }

    [Fact]
    public void ByMinute_Daily_ExpandsToMultipleOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=8;BYMINUTE=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 2, 9, 0, 0),
            new DateTime(2020, 1, 2, 9, 30, 0),
            new DateTime(2020, 1, 3, 9, 0, 0),
            new DateTime(2020, 1, 3, 9, 30, 0),
            new DateTime(2020, 1, 4, 9, 0, 0),
            new DateTime(2020, 1, 4, 9, 30, 0));
    }

    [Fact]
    public void ByMinute_InvalidValue_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYMINUTE=60"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYMINUTE=-1"));
    }

    [Fact]
    public void ByMinute_Weekly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4;BYDAY=MO;BYMINUTE=0,30");
        var startDate = new DateTime(2020, 1, 6, 9, 0, 0); // Monday
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 6, 9, 0, 0),
            new DateTime(2020, 1, 6, 9, 30, 0),
            new DateTime(2020, 1, 13, 9, 0, 0),
            new DateTime(2020, 1, 13, 9, 30, 0));
    }

    [Fact]
    public void ByMinute_Monthly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=4;BYMONTHDAY=1;BYMINUTE=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 2, 1, 9, 0, 0),
            new DateTime(2020, 2, 1, 9, 30, 0));
    }

    [Fact]
    public void ByMinute_Yearly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;COUNT=4;BYMONTH=1;BYMONTHDAY=1;BYMINUTE=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2021, 1, 1, 9, 0, 0),
            new DateTime(2021, 1, 1, 9, 30, 0));
    }

    [Fact]
    public void ByMinuteAndBySecond_Combined()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=4;BYMINUTE=0,30;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 1, 9, 30, 30));
    }

    [Fact]
    public void ByHour_Daily_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,12,15,18");
        var text = rrule.Text;
        Assert.Equal("FREQ=DAILY;BYHOUR=9,12,15,18", text);
    }

    [Fact]
    public void ByHour_Daily_ExpandsToMultipleOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=8;BYHOUR=9,15");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 15, 0, 0),
            new DateTime(2020, 1, 2, 9, 0, 0),
            new DateTime(2020, 1, 2, 15, 0, 0),
            new DateTime(2020, 1, 3, 9, 0, 0),
            new DateTime(2020, 1, 3, 15, 0, 0),
            new DateTime(2020, 1, 4, 9, 0, 0),
            new DateTime(2020, 1, 4, 15, 0, 0));
    }

    [Fact]
    public void ByHour_InvalidValue_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=24"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=-1"));
    }

    [Fact]
    public void ByHour_Weekly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4;BYDAY=MO;BYHOUR=9,15");
        var startDate = new DateTime(2020, 1, 6, 9, 0, 0); // Monday
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 6, 9, 0, 0),
            new DateTime(2020, 1, 6, 15, 0, 0),
            new DateTime(2020, 1, 13, 9, 0, 0),
            new DateTime(2020, 1, 13, 15, 0, 0));
    }

    [Fact]
    public void ByHour_Monthly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=4;BYMONTHDAY=1;BYHOUR=9,15");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 15, 0, 0),
            new DateTime(2020, 2, 1, 9, 0, 0),
            new DateTime(2020, 2, 1, 15, 0, 0));
    }

    [Fact]
    public void ByHour_Yearly()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;COUNT=4;BYMONTH=1;BYMONTHDAY=1;BYHOUR=9,15");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 15, 0, 0),
            new DateTime(2021, 1, 1, 9, 0, 0),
            new DateTime(2021, 1, 1, 15, 0, 0));
    }

    [Fact]
    public void ByHour_ByMinute_BySecond_AllCombined()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=8;BYHOUR=9,15;BYMINUTE=0,30;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 1, 9, 30, 30),
            new DateTime(2020, 1, 1, 15, 0, 0),
            new DateTime(2020, 1, 1, 15, 0, 30),
            new DateTime(2020, 1, 1, 15, 30, 0),
            new DateTime(2020, 1, 1, 15, 30, 30));
    }

    [Fact]
    public void ByHour_MidnightAndNoon()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=4;BYHOUR=0,12");
        var startDate = new DateTime(2020, 1, 1, 0, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 0, 0, 0),
            new DateTime(2020, 1, 1, 12, 0, 0),
            new DateTime(2020, 1, 2, 0, 0, 0),
            new DateTime(2020, 1, 2, 12, 0, 0));
    }

    [Fact]
    public void ByHour_23_LastHourOfDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=2;BYHOUR=23");
        var startDate = new DateTime(2020, 1, 1, 0, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 23, 0, 0),
            new DateTime(2020, 1, 2, 23, 0, 0));
    }

    [Fact]
    public void Daily_ByHourEarlierThanTheStartTimeResumesTheNextDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
        var startDate = new DateTime(2024, 01, 01, 14, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 01, 02, 09, 00, 00),
            new DateTime(2024, 01, 03, 09, 00, 00),
            new DateTime(2024, 01, 04, 09, 00, 00));
    }

    [Fact]
    public void Daily_ByHourLaterThanTheStartTimeStartsTheSameDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
        var startDate = new DateTime(2024, 01, 01, 08, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 01, 01, 09, 00, 00),
            new DateTime(2024, 01, 02, 09, 00, 00),
            new DateTime(2024, 01, 03, 09, 00, 00));
    }

    [Fact]
    public void Weekly_ByHourEarlierThanTheStartTimeResumesTheNextWeek()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;BYHOUR=9");
        var startDate = new DateTime(2024, 01, 01, 14, 00, 00); // Monday
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 01, 08, 09, 00, 00),
            new DateTime(2024, 01, 15, 09, 00, 00),
            new DateTime(2024, 01, 22, 09, 00, 00));
    }

    [Fact]
    public void Hourly_ByMinuteEarlierThanTheStartTimeResumesTheNextHour()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;BYMINUTE=15");
        var startDate = new DateTime(2024, 01, 01, 09, 30, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 01, 01, 10, 15, 00),
            new DateTime(2024, 01, 01, 11, 15, 00),
            new DateTime(2024, 01, 01, 12, 15, 00));
    }

    [Fact]
    public void Monthly_ByHourEarlierThanTheStartTimeResumesTheNextMonth()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYHOUR=9");
        var startDate = new DateTime(2024, 01, 15, 14, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2024, 02, 15, 09, 00, 00),
            new DateTime(2024, 03, 15, 09, 00, 00),
            new DateTime(2024, 04, 15, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByHourEarlierThanTheStartTimeResumesTheNextYear()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYHOUR=9");
        var startDate = new DateTime(2024, 06, 15, 14, 00, 00);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrencesStartWith(occurrences,
            new DateTime(2025, 06, 15, 09, 00, 00),
            new DateTime(2026, 06, 15, 09, 00, 00),
            new DateTime(2027, 06, 15, 09, 00, 00));
    }

    [Fact]
    public void Secondly_Every5Seconds()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;INTERVAL=5;COUNT=5");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 5),
            new DateTime(2020, 1, 1, 9, 0, 10),
            new DateTime(2020, 1, 1, 9, 0, 15),
            new DateTime(2020, 1, 1, 9, 0, 20));
    }

    [Fact]
    public void Secondly_WithByHour()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;INTERVAL=30;COUNT=4;BYHOUR=9");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 1, 0),
            new DateTime(2020, 1, 1, 9, 1, 30));
    }

    [Fact]
    public void Secondly_WithByMinute()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;INTERVAL=15;COUNT=4;BYMINUTE=0");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 15),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 0, 45));
    }

    [Fact]
    public void Secondly_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;INTERVAL=30;BYHOUR=9,15;BYMINUTE=0,30");
        var text = rrule.Text;
        Assert.Equal("FREQ=SECONDLY;INTERVAL=30;BYHOUR=9,15;BYMINUTE=0,30", text);
    }

    [Fact]
    public void Minutely_Every5Minutes()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=5;COUNT=5");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 5, 0),
            new DateTime(2020, 1, 1, 9, 10, 0),
            new DateTime(2020, 1, 1, 9, 15, 0),
            new DateTime(2020, 1, 1, 9, 20, 0));
    }

    [Fact]
    public void Minutely_WithByHour()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=15;COUNT=4;BYHOUR=9,15");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 15, 0),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 1, 9, 45, 0));
    }

    [Fact]
    public void Minutely_WithBySecond()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=1;COUNT=4;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 1, 0),
            new DateTime(2020, 1, 1, 9, 1, 30));
    }

    [Fact]
    public void Minutely_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=15;BYHOUR=9,15;BYSECOND=0,30");
        var text = rrule.Text;
        Assert.Equal("FREQ=MINUTELY;INTERVAL=15;BYHOUR=9,15;BYSECOND=0,30", text);
    }

    [Fact]
    public void Hourly_Every2Hours()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;INTERVAL=2;COUNT=5");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 11, 0, 0),
            new DateTime(2020, 1, 1, 13, 0, 0),
            new DateTime(2020, 1, 1, 15, 0, 0),
            new DateTime(2020, 1, 1, 17, 0, 0));
    }

    [Fact]
    public void Hourly_WithByMinute()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;INTERVAL=2;COUNT=6;BYMINUTE=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 1, 11, 0, 0),
            new DateTime(2020, 1, 1, 11, 30, 0),
            new DateTime(2020, 1, 1, 13, 0, 0),
            new DateTime(2020, 1, 1, 13, 30, 0));
    }

    [Fact]
    public void Hourly_WithByMinuteAndBySecond()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;INTERVAL=1;COUNT=4;BYMINUTE=0,30;BYSECOND=0,30");
        var startDate = new DateTime(2020, 1, 1, 9, 0, 0);
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 1, 9, 0, 0),
            new DateTime(2020, 1, 1, 9, 0, 30),
            new DateTime(2020, 1, 1, 9, 30, 0),
            new DateTime(2020, 1, 1, 9, 30, 30));
    }

    [Fact]
    public void Hourly_ParseAndSerialize()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;INTERVAL=3;BYMINUTE=0,30;BYSECOND=0,15,30,45");
        var text = rrule.Text;
        Assert.Equal("FREQ=HOURLY;INTERVAL=3;BYMINUTE=0,30;BYSECOND=0,15,30,45", text);
    }

    [Fact]
    public void Hourly_WithByDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;COUNT=3;BYDAY=MO");
        var startDate = new DateTime(2020, 1, 6, 9, 0, 0); // Monday
        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences,
            new DateTime(2020, 1, 6, 9, 0, 0),
            new DateTime(2020, 1, 6, 10, 0, 0),
            new DateTime(2020, 1, 6, 11, 0, 0));
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Secondly_GetHumanText_en_us()
    {
        TestGetHumanText("FREQ=SECONDLY", "en-US", "every second");
        TestGetHumanText("FREQ=SECONDLY;INTERVAL=5", "en-US", "every 5 seconds");
        TestGetHumanText("FREQ=SECONDLY;COUNT=10", "en-US", "every second for 10 times");
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Secondly_GetHumanText_fr_fr()
    {
        TestGetHumanText("FREQ=SECONDLY", "fr-FR", "toutes les secondes");
        TestGetHumanText("FREQ=SECONDLY;INTERVAL=5", "fr-FR", "toutes les 5 secondes");
        TestGetHumanText("FREQ=SECONDLY;COUNT=10", "fr-FR", "toutes les secondes pour 10 fois");
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Minutely_GetHumanText_en_us()
    {
        TestGetHumanText("FREQ=MINUTELY", "en-US", "every minute");
        TestGetHumanText("FREQ=MINUTELY;INTERVAL=15", "en-US", "every 15 minutes");
        TestGetHumanText("FREQ=MINUTELY;COUNT=10", "en-US", "every minute for 10 times");
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Minutely_GetHumanText_fr_fr()
    {
        TestGetHumanText("FREQ=MINUTELY", "fr-FR", "toutes les minutes");
        TestGetHumanText("FREQ=MINUTELY;INTERVAL=15", "fr-FR", "toutes les 15 minutes");
        TestGetHumanText("FREQ=MINUTELY;COUNT=10", "fr-FR", "toutes les minutes pour 10 fois");
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Hourly_GetHumanText_en_us()
    {
        TestGetHumanText("FREQ=HOURLY", "en-US", "every hour");
        TestGetHumanText("FREQ=HOURLY;INTERVAL=2", "en-US", "every other hour");
        TestGetHumanText("FREQ=HOURLY;INTERVAL=3", "en-US", "every 3 hours");
        TestGetHumanText("FREQ=HOURLY;COUNT=10", "en-US", "every hour for 10 times");
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Hourly_GetHumanText_fr_fr()
    {
        TestGetHumanText("FREQ=HOURLY", "fr-FR", "toutes les heures");
        TestGetHumanText("FREQ=HOURLY;INTERVAL=3", "fr-FR", "toutes les 3 heures");
        TestGetHumanText("FREQ=HOURLY;COUNT=10", "fr-FR", "toutes les heures pour 10 fois");
    }

    [Fact]
    public void GetNextOccurrences_Utc_MatchesTheDateTimeOverload()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        var startDate = new DateTime(2024, 03, 09, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 03, 10, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2024, 03, 11, 09, 00, 00, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrences_NullTimeZone_ThrowsBeforeEnumeration()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Throws<ArgumentNullException>(() => rrule.GetNextOccurrences(new DateTime(2024, 01, 01), timeZone: null!));
    }

    [Fact]
    public void GetNextOccurrence_TimeZone_ReturnsNullWhenTheRuleIsExhausted()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=0");

        Assert.Null(rrule.GetNextOccurrence(new DateTime(2024, 01, 01, 09, 00, 00), TimeZoneInfo.Utc));
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    // An IANA identifier does not resolve on Windows when globalization is invariant.
    private static TimeZoneInfo NewYork => TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static TimeZoneInfo Sydney => TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    [Fact]
    public void Daily_TimeZone_AcrossSpringForward_KeepsTheWallClockTime()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        var startDate = new DateTime(2024, 03, 09, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 09, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 11, 09, 00, 00, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void Daily_TimeZone_AcrossFallBack_KeepsTheWallClockTime()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        var startDate = new DateTime(2024, 11, 02, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 11, 02, 09, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 11, 03, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 11, 04, 09, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void Daily_TimeZone_InvalidLocalTime_UsesTheOffsetBeforeTheGap()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0;COUNT=2");
        var startDate = new DateTime(2024, 03, 09, 02, 30, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        // 02:30 does not exist on 2024-03-10: read with the -05:00 offset in effect before the gap, it is 07:30Z
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 09, 02, 30, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 03, 30, 00, TimeSpan.FromHours(-4)));

        Assert.Equal(new DateTime(2024, 03, 10, 07, 30, 00, DateTimeKind.Utc), occurrences[1].UtcDateTime);
    }

    [Fact]
    public void Daily_TimeZone_AmbiguousLocalTime_UsesTheFirstOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=1;BYMINUTE=30;BYSECOND=0;COUNT=3");
        var startDate = new DateTime(2024, 11, 02, 01, 30, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        // 01:30 happens twice on 2024-11-03 and RFC 5545 keeps the first one, at the -04:00 offset
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 11, 02, 01, 30, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 11, 03, 01, 30, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 11, 04, 01, 30, 00, TimeSpan.FromHours(-5)));

        Assert.Equal(new DateTime(2024, 11, 03, 05, 30, 00, DateTimeKind.Utc), occurrences[1].UtcDateTime);
    }

    [Fact]
    public void Hourly_TimeZone_AcrossSpringForward_DropsTheInstantRepeatedByTheGap()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;COUNT=5");
        var startDate = new DateTime(2024, 03, 10, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        // 02:00 does not exist and is read at the -05:00 offset in effect before the gap, which is the instant
        // 03:00 already denotes. RFC 5545 section 3.8.5.3 keeps only one of the two, and the duplicate does not
        // count towards COUNT, so a fifth distinct occurrence is produced instead.
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 10, 00, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 01, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 03, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 04, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 05, 00, 00, TimeSpan.FromHours(-4)));

        Assert.HasCount(occurrences.Length, occurrences.Select(occurrence => occurrence.UtcDateTime).Distinct());
    }

    [Fact]
    public void Minutely_TimeZone_AcrossSpringForward_DropsTheInstantsRepeatedByTheGap()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=20;COUNT=6");
        var startDate = new DateTime(2024, 03, 10, 01, 20, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        // Every local time in the 02:00 gap is read at the -05:00 offset in effect before it, mapping the whole
        // gap onto the instants of the hour that follows. Those repeats are not adjacent to the instances they
        // duplicate, so suppressing them keeps the recurrence set increasing.
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 10, 01, 20, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 01, 40, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 03, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 03, 20, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 03, 40, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 04, 00, 00, TimeSpan.FromHours(-4)));

        var instants = occurrences.Select(occurrence => occurrence.UtcDateTime).ToArray();
        Assert.HasCount(instants.Length, instants.Distinct());
        Assert.Equal(instants.Order().ToArray(), instants);
    }

    [Fact]
    public void Hourly_TimeZone_AcrossFallBack_SkipsTheRepeatedHour()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;COUNT=4");
        var startDate = new DateTime(2024, 11, 03, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        // Every wall clock keeps its first reading, so the second pass over 01:00 (06:00Z) is never produced
        Assert.Equal(new DateTime(2024, 11, 03, 04, 00, 00, DateTimeKind.Utc), occurrences[0].UtcDateTime);
        Assert.Equal(new DateTime(2024, 11, 03, 05, 00, 00, DateTimeKind.Utc), occurrences[1].UtcDateTime);
        Assert.Equal(new DateTime(2024, 11, 03, 07, 00, 00, DateTimeKind.Utc), occurrences[2].UtcDateTime);
        Assert.Equal(new DateTime(2024, 11, 03, 08, 00, 00, DateTimeKind.Utc), occurrences[3].UtcDateTime);
    }

    [Fact]
    public void Daily_TimeZone_SouthernHemisphere_AcrossDaylightSavingStart()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=2");
        var startDate = new DateTime(2024, 10, 05, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, Sydney);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 10, 05, 09, 00, 00, TimeSpan.FromHours(10)),
            new DateTimeOffset(2024, 10, 06, 09, 00, 00, TimeSpan.FromHours(11)));
    }

    [Fact]
    public void Daily_TimeZone_SouthernHemisphere_InvalidLocalTime()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0;COUNT=1");
        var startDate = new DateTime(2024, 10, 06, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, Sydney);

        AssertOccurrences(occurrences, new DateTimeOffset(2024, 10, 06, 03, 30, 00, TimeSpan.FromHours(11)));
    }

    [Fact]
    public void Daily_TimeZone_SouthernHemisphere_AmbiguousLocalTime()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0;COUNT=1");
        var startDate = new DateTime(2024, 04, 07, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, Sydney);

        AssertOccurrences(occurrences, new DateTimeOffset(2024, 04, 07, 02, 30, 00, TimeSpan.FromHours(11)));
    }

    [Fact]
    public void Daily_TimeZone_UtcUntil_IsComparedAsAnInstantNotAsAWallClock()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240310T120000Z");
        var startDate = new DateTime(2024, 03, 08, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        // 2024-03-10 09:00-04:00 is 13:00Z, which is past the UNTIL instant, so it is excluded.
        // Comparing the wall clock instead would have kept it.
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 08, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void Daily_TimeZone_Count_IsUnaffectedByATransition()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=5");
        var startDate = new DateTime(2024, 03, 08, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork).ToArray();

        Assert.HasCount(5, occurrences);
        Assert.All(occurrences, occurrence => Assert.Equal(new TimeSpan(09, 00, 00), occurrence.TimeOfDay));
        Assert.Equal(
            new[] { -5, -5, -4, -4, -4 },
            occurrences.Select(occurrence => (int)occurrence.Offset.TotalHours).ToArray());
    }

    [Fact]
    public void Monthly_TimeZone_BySetPosition_AcrossATransition()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=SU;BYSETPOS=2;BYHOUR=2;BYMINUTE=30;BYSECOND=0;COUNT=2");
        var startDate = new DateTime(2024, 03, 01, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        // The second Sunday of March 2024 is the 10th, where 02:30 falls in the gap
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 10, 03, 30, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 04, 14, 02, 30, 00, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZoneId_MatchesTheTimeZoneInfoOverload()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=4");
        var startDate = new DateTime(2024, 03, 09, 09, 00, 00);

        var expected = rrule.GetNextOccurrences(startDate, NewYork);
        var actual = rrule.GetNextOccurrences(startDate, "America/New_York");

        AssertOccurrences(actual, expected.ToArray());
    }

    [Fact]
    public void GetNextOccurrences_UnknownTimeZoneId_ThrowsBeforeEnumeration()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Throws<TimeZoneNotFoundException>(() => rrule.GetNextOccurrences(new DateTime(2024, 01, 01), "Not/AZone"));
    }

    [Fact]
    public void GetNextOccurrences_ThroughIRecurrenceRule_UsesTheInstantBasedUntil()
    {
        IRecurrenceRule rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240310T120000Z");
        var startDate = new DateTime(2024, 03, 08, 09, 00, 00);

        // Extension methods bind statically, so this is what guards the dispatch back to RecurrenceRule
        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 08, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void GetNextOccurrences_DateTimeOffsetStart_IsReducedToTheTimeZoneWallClock()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=1");
        var startDate = new DateTimeOffset(2024, 03, 09, 12, 00, 00, TimeSpan.Zero);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        AssertOccurrences(occurrences, new DateTimeOffset(2024, 03, 09, 07, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void GetNextOccurrence_TimeZone_ReturnsTheFirstOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9;BYMINUTE=0;BYSECOND=0");
        var startDate = new DateTime(2024, 03, 10, 00, 00, 00);

        var occurrence = rrule.GetNextOccurrence(startDate, NewYork);

        Assert.NotNull(occurrence);
        Assert.Equal(new DateTime(2024, 03, 10, 09, 00, 00), occurrence.Value.DateTime);
        Assert.Equal(TimeSpan.FromHours(-4), occurrence.Value.Offset);
    }
#endif

    private static void TestGetHumanText(string rruleText, string cultureInfo, string expectedText)
    {
#if INVARIANT_GLOBALIZATION_MODE_ENABLED
        var culture = cultureInfo is "en-US" or "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureInfo);
#else
        var culture = CultureInfo.GetCultureInfo(cultureInfo);
#endif
        var rrule = RecurrenceRule.Parse(rruleText);
        var text = rrule.GetHumanText(culture);
        Assert.Equal(expectedText, text);
    }
}
