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
