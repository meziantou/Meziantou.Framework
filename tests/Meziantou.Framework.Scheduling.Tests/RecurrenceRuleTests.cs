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
    public void GetNextOccurrence_DateTimeOffset_UtcUntil_IsComparedByInstant()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;UNTIL=20260101T010000Z");

        // 00:30Z is before UNTIL, although its local time is after it
        Assert.Equal(
            new DateTimeOffset(2026, 01, 01, 02, 30, 00, TimeSpan.FromHours(2)),
            rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 02, 30, 00, TimeSpan.FromHours(2))));

        // 05:30Z is after UNTIL, although its local time is before it
        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 00, 30, 00, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_LocalUntil_IsComparedByInstant()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY");
        rrule.EndDate = new DateTime(2026, 01, 01, 01, 00, 00, DateTimeKind.Utc).ToLocalTime();

        Assert.Equal(
            new DateTimeOffset(2026, 01, 01, 02, 30, 00, TimeSpan.FromHours(2)),
            rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 02, 30, 00, TimeSpan.FromHours(2))));
        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 00, 30, 00, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_FloatingUntil_IsComparedByWallClock()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;UNTIL=20260101T010000");

        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 02, 30, 00, TimeSpan.FromHours(2))));
        Assert.Equal(
            new DateTimeOffset(2026, 01, 01, 00, 30, 00, TimeSpan.FromHours(-5)),
            rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 01, 00, 30, 00, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffset_UtcUntilAtTheEndOfTheDateRange()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        Assert.Equal(
            new DateTimeOffset(9999, 12, 31, 09, 00, 00, TimeSpan.FromHours(2)),
            rrule.GetNextOccurrence(new DateTimeOffset(9999, 12, 31, 09, 00, 00, TimeSpan.FromHours(2))));

        rrule.EndDate = new DateTime(1, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5))));
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

    // Synthetic time zones work when globalization is invariant, and keep the tests independent of the time zone database.
    private static TimeZoneInfo CreateTestTimeZone(TimeSpan baseUtcOffset, TimeSpan daylightDelta)
    {
        // Daylight saving time starts on the last Sunday of March at 02:00 and ends on the last Sunday of October at 03:00 daylight time
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 5, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), month: 10, week: 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, daylightDelta, daylightStart, daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Zone", baseUtcOffset, "Test", "Test Standard", "Test Daylight", [rule]);
    }

    private static TimeZoneInfo TestParis => CreateTestTimeZone(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

    private static TimeZoneInfo TestNewYork
    {
        get
        {
            var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
            var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd);
            return TimeZoneInfo.CreateCustomTimeZone("Test/New_York", TimeSpan.FromHours(-5), "Test Eastern", "EST", "EDT", [rule]);
        }
    }

    [Fact]
    public void Minutely_TimeZone_AcrossSpringForward_KeepsTheOccurrencesAfterAMovedOne()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=40;COUNT=7");
        var startDate = new DateTime(2026, 03, 29, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TestParis).ToArray();

        // 02:00 and 02:40 do not exist and move forward by the one-hour gap, after 03:20, which is still an occurrence
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 00, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 00, 40, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 01, 20, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 20, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 40, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 04, 00, 00, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Minutely_TimeZone_AcrossSpringForward_CountsInTheOrderOfTheWallClockTimes()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=40;COUNT=5");
        var startDate = new DateTime(2026, 03, 29, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TestParis);

        // The fifth instance is 02:40, read as 03:40; 03:20 is the sixth one
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 00, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 00, 40, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 01, 20, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 40, 00, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Minutely_TimeZone_AcrossSpringForward_WithoutCount_IsInIncreasingOrder()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=45");
        var startDate = new DateTime(2026, 03, 08, 00, 45, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TestNewYork).Take(5);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 08, 00, 45, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 03, 08, 01, 30, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 03, 08, 03, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 03, 08, 03, 15, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 03, 08, 03, 45, 00, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void Minutely_TimeZone_AcrossALongGap_IsInIncreasingOrder()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=100;COUNT=6");
        var startDate = new DateTime(2026, 03, 29, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, CreateTestTimeZone(TimeSpan.Zero, TimeSpan.FromHours(3)));

        // The gap skips 02:00 to 05:00, so 03:20 moves to 06:20, after 05:00 but before 06:40
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 00, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2026, 03, 29, 01, 40, 00, TimeSpan.Zero),
            new DateTimeOffset(2026, 03, 29, 05, 00, 00, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 03, 29, 06, 20, 00, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 03, 29, 06, 40, 00, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 03, 29, 08, 20, 00, TimeSpan.FromHours(3)));
    }

    [Fact]
    public void Minutely_TimeZone_UtcUntil_SkipsAMovedOccurrenceButKeepsTheEarlierOnesAfterIt()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=40;UNTIL=20260329T012500Z");
        var startDate = new DateTime(2026, 03, 29, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TestParis);

        // 02:40 is read as 03:40+02:00, past UNTIL (03:25+02:00), but 03:20 is not
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 00, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 00, 40, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 01, 20, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 20, 00, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GetNextOccurrences_DateTimeOffsetStart_InTheSecondPassOfARepeatedHour_ReturnsNothingBeforeIt()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");

        // 06:40Z is the second 01:40 of the day, and the wall-clock time 01:40 denotes the first one, 05:40Z
        var occurrences = rrule.GetNextOccurrences(new DateTimeOffset(2026, 11, 01, 01, 40, 00, TimeSpan.FromHours(-5)), TestNewYork);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 11, 02, 01, 40, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 11, 03, 01, 40, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void GetNextOccurrence_DateTimeOffsetStart_InTheSecondPassOfARepeatedHour_ReturnsNothingBeforeIt()
    {
        IRecurrenceRule rrule = RecurrenceRule.Parse("FREQ=MINUTELY");

        var occurrence = rrule.GetNextOccurrence(new DateTimeOffset(2026, 11, 01, 01, 40, 00, TimeSpan.FromHours(-5)), TestNewYork);

        Assert.Equal(new DateTimeOffset(2026, 11, 01, 02, 00, 00, TimeSpan.FromHours(-5)), occurrence);
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AfterTheMaximumDate_EndsTheEnumeration()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        var occurrences = rrule.GetNextOccurrences(new DateTime(9999, 12, 30, 23, 00, 00), TestNewYork).Take(3);

        AssertOccurrences(occurrences, new DateTimeOffset(9999, 12, 30, 23, 00, 00, TimeSpan.FromHours(-5)));
        Assert.Null(rrule.GetNextOccurrence(new DateTime(9999, 12, 31, 23, 00, 00), TestNewYork));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_BeforeTheMinimumDate_SkipsTheOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");

        var occurrences = rrule.GetNextOccurrences(DateTime.MinValue, TestParis);

        // January 1 at midnight is December 31 of year 0 in UTC. It is still an instance of the recurrence.
        AssertOccurrences(occurrences,
            new DateTimeOffset(0001, 01, 02, 00, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(0001, 01, 03, 00, 00, 00, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void GetNextOccurrences_DateTimeOffsetStart_AtTheLimits_DoesNotThrow()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.Empty(rrule.GetNextOccurrences(DateTimeOffset.MaxValue, TestParis));
        AssertOccurrences(rrule.GetNextOccurrences(DateTimeOffset.MinValue, TestNewYork).Take(1), new DateTimeOffset(0001, 01, 01, 00, 00, 00, TimeSpan.FromHours(-5)));
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
        // gap onto the instants of the hour that follows. Each of those instants is returned once, in increasing order.
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
    public void Until_Floating_IsComparedAsAWallClockInATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240310T090000");
        var startDate = new DateTime(2024, 03, 09, 09, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, NewYork);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 09, 00, 00, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void Until_Date_IncludesItsDayInATimeZoneWestOfUtc()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240103");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2024, 01, 01), NewYork);

        // Read as midnight UTC, January 3 at midnight in New York (05:00Z) would have been excluded
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 01, 01, 00, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 01, 02, 00, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 01, 03, 00, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void Until_Date_IncludesTheTimedOccurrencesOfItsDayInATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240103");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2024, 01, 01, 21, 00, 00), NewYork).ToArray();

        Assert.HasCount(3, occurrences);
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 01, 01, 21, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 01, 02, 21, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 01, 03, 21, 00, 00, TimeSpan.FromHours(-5)));
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

    [Fact]
    public void Minutely_TimeZone_Paris_AcrossSpringForward_KeepsTheOccurrencesAfterAMovedOne()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;INTERVAL=40;COUNT=7");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 03, 29, 00, 00, 00), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 00, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 00, 40, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 01, 20, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 20, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 03, 40, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 04, 00, 00, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AtTheLimitsOfTheSystemTimeZones_DoesNotThrow()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");

        Assert.HasCount(1, rrule.GetNextOccurrences(new DateTime(9999, 12, 30, 23, 00, 00), NewYork).Take(3).ToList());
        Assert.NotEmpty(rrule.GetNextOccurrences(new DateTime(1, 1, 1), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).Take(1).ToList());
    }
#endif

    [Fact]
    public void Hourly_ByHourLimitsTheHours()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;BYHOUR=9,10;COUNT=4");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 00, 00),
                new DateTime(2025, 01, 15, 10, 00, 00),
                new DateTime(2025, 01, 16, 09, 00, 00),
                new DateTime(2025, 01, 16, 10, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Minutely_ByMinuteLimitsTheMinutes()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MINUTELY;BYMINUTE=0,30;COUNT=3");
        var startDate = new DateTime(2025, 01, 15, 09, 10, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 30, 00),
                new DateTime(2025, 01, 15, 10, 00, 00),
                new DateTime(2025, 01, 15, 10, 30, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Secondly_BySecondLimitsTheSeconds()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;BYSECOND=0;COUNT=3");
        var startDate = new DateTime(2025, 01, 15, 09, 00, 30);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 01, 00),
                new DateTime(2025, 01, 15, 09, 02, 00),
                new DateTime(2025, 01, 15, 09, 03, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=MINUTELY;BYHOUR=9", "FREQ=MINUTELY")]
    [InlineData("FREQ=SECONDLY;BYHOUR=9", "FREQ=SECONDLY")]
    [InlineData("FREQ=HOURLY;BYHOUR=9,10", "FREQ=HOURLY")]
    [InlineData("FREQ=SECONDLY;BYMINUTE=15,17", "FREQ=SECONDLY")]
    [InlineData("FREQ=MINUTELY;BYMINUTE=15,17", "FREQ=MINUTELY")]
    [InlineData("FREQ=SECONDLY;BYSECOND=30,45", "FREQ=SECONDLY")]
    [InlineData("FREQ=HOURLY;INTERVAL=5;BYHOUR=9,14,19", "FREQ=HOURLY;INTERVAL=5")]
    public void LimitingTimeParts_KeepTheSubSecondPrecisionOfTheStartDate(string limitedRuleText, string ruleText)
    {
        var startDate = new DateTime(2026, 09, 14, 09, 15, 30, 500);
        var limitedRule = RecurrenceRule.Parse(limitedRuleText);
        var rule = RecurrenceRule.Parse(ruleText);

        var expected = rule.GetNextOccurrences(startDate)
            .Where(occurrence => IsIncluded(limitedRule.ByHours, occurrence.Hour) && IsIncluded(limitedRule.ByMinutes, occurrence.Minute) && IsIncluded(limitedRule.BySeconds, occurrence.Second))
            .Take(5)
            .ToArray();

        Assert.Equal(startDate, expected[0]);
        Assert.Equal(expected, limitedRule.GetNextOccurrences(startDate).Take(5).ToArray());

        static bool IsIncluded(IList<int>? values, int value) => values is null || values.Count is 0 || values.Contains(value);
    }

    [Fact]
    public void ExpandingTimeParts_KeepTheSubSecondPrecisionOfTheStartDate()
    {
        var startDate = new DateTime(2026, 09, 14, 09, 15, 30, 500);

        Assert.Equal(
            [
                new DateTime(2026, 09, 14, 09, 15, 30, 500),
                new DateTime(2026, 09, 14, 10, 15, 30, 500),
                new DateTime(2026, 09, 15, 09, 15, 30, 500),
            ],
            RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,10").GetNextOccurrences(startDate).Take(3).ToArray());

        Assert.Equal(
            [
                new DateTime(2026, 09, 14, 09, 15, 30, 500),
                new DateTime(2026, 09, 14, 09, 16, 00, 500),
                new DateTime(2026, 09, 14, 09, 16, 30, 500),
            ],
            RecurrenceRule.Parse("FREQ=MINUTELY;BYSECOND=0,30").GetNextOccurrences(startDate).Take(3).ToArray());

        Assert.Equal(
            [
                new DateTime(2026, 09, 14, 10, 00, 00, 500),
                new DateTime(2026, 09, 14, 11, 00, 00, 500),
            ],
            RecurrenceRule.Parse("FREQ=HOURLY;BYMINUTE=0;BYSECOND=0").GetNextOccurrences(startDate).Take(2).ToArray());

        Assert.Equal(
            [
                new DateTime(2026, 09, 14, 09, 15, 30, 500),
                new DateTime(2026, 09, 15, 09, 15, 30, 500),
            ],
            RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9;BYMINUTE=15;BYSECOND=30;BYSETPOS=1").GetNextOccurrences(startDate).Take(2).ToArray());
    }

    [Fact]
    public void Until_DateTime_IsComparedAtSecondPrecisionWithAFractionalStartDate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240103T100000");
        var startDate = new DateTime(2024, 01, 01, 10, 00, 00, 123);

        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 01, 01, 10, 00, 00, 123),
            new DateTime(2024, 01, 02, 10, 00, 00, 123),
            new DateTime(2024, 01, 03, 10, 00, 00, 123));

        AssertOccurrences(rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc),
            new DateTimeOffset(2024, 01, 01, 10, 00, 00, 123, TimeSpan.Zero),
            new DateTimeOffset(2024, 01, 02, 10, 00, 00, 123, TimeSpan.Zero),
            new DateTimeOffset(2024, 01, 03, 10, 00, 00, 123, TimeSpan.Zero));

        var offset = TimeSpan.FromHours(2);
        Assert.Equal(new DateTimeOffset(2024, 01, 03, 10, 00, 00, 123, offset), rrule.GetNextOccurrence(new DateTimeOffset(2024, 01, 03, 10, 00, 00, 123, offset)));
    }

    [Fact]
    public void Until_UtcDateTime_IsComparedAtSecondPrecisionWithAFractionalStartDate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;UNTIL=20240101T100001Z");
        var startDate = new DateTime(2024, 01, 01, 10, 00, 00, 500, DateTimeKind.Utc);

        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 01, 01, 10, 00, 00, 500, DateTimeKind.Utc),
            new DateTime(2024, 01, 01, 10, 00, 01, 500, DateTimeKind.Utc));

        AssertOccurrences(rrule.GetNextOccurrences(new DateTimeOffset(startDate), TimeZoneInfo.Utc),
            new DateTimeOffset(2024, 01, 01, 10, 00, 00, 500, TimeSpan.Zero),
            new DateTimeOffset(2024, 01, 01, 10, 00, 01, 500, TimeSpan.Zero));

        var offset = TimeSpan.FromHours(-5);
        Assert.Equal(new DateTimeOffset(2024, 01, 01, 05, 00, 01, 700, offset), rrule.GetNextOccurrence(new DateTimeOffset(2024, 01, 01, 05, 00, 01, 700, offset)));
        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(2024, 01, 01, 05, 00, 02, offset)));
    }

    [Fact]
    public void Until_SettingAFractionalEndDate_BoundsTheOccurrencesAtSecondPrecision()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY");
        rrule.EndDate = new DateTime(2024, 01, 01, 10, 00, 01, 200);

        Assert.Equal("FREQ=SECONDLY;UNTIL=20240101T100001", rrule.Text);
        AssertOccurrences(rrule.GetNextOccurrences(new DateTime(2024, 01, 01, 10, 00, 00, 500)),
            new DateTime(2024, 01, 01, 10, 00, 00, 500),
            new DateTime(2024, 01, 01, 10, 00, 01, 500));
    }

    [Fact]
    public void Hourly_ByYearDayLimitsTheDays()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;BYYEARDAY=-1;BYHOUR=0,12;COUNT=3");
        var startDate = new DateTime(2025, 01, 01, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 12, 31, 00, 00, 00),
                new DateTime(2025, 12, 31, 12, 00, 00),
                new DateTime(2026, 12, 31, 00, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=SECONDLY;BYYEARDAY=1,-1;BYSECOND=0,30")]
    [InlineData("FREQ=MINUTELY;BYYEARDAY=100;BYMINUTE=0,30")]
    [InlineData("FREQ=HOURLY;BYYEARDAY=-100;BYHOUR=9,10")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20,-1;BYDAY=MO")]
    public void Text_RoundTripsTheLimitingParts(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);

        Assert.Equal(rruleText, rrule.Text);
    }

    [Fact]
    public void Daily_UnsortedByHourProducesIncreasingOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=17,9;COUNT=3");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        Assert.Equal("FREQ=DAILY;COUNT=3;BYHOUR=17,9", rrule.Text);
        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 00, 00),
                new DateTime(2025, 01, 15, 17, 00, 00),
                new DateTime(2025, 01, 16, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Daily_UnsortedByHourDoesNotStopBeforeUntil()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=17,9;BYMINUTE=30,0;UNTIL=20250116T100000Z");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 00, 00),
                new DateTime(2025, 01, 15, 09, 30, 00),
                new DateTime(2025, 01, 15, 17, 00, 00),
                new DateTime(2025, 01, 15, 17, 30, 00),
                new DateTime(2025, 01, 16, 09, 00, 00),
                new DateTime(2025, 01, 16, 09, 30, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Daily_TimeZone_UnsortedByHourKeepsEveryOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=17,9;COUNT=4");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        var occurrences = rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2025, 01, 15, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2025, 01, 15, 17, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2025, 01, 16, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2025, 01, 16, 17, 00, 00, TimeSpan.Zero));
    }

    [Fact]
    public void Daily_DuplicateByHourProducesDistinctOccurrences()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,9;COUNT=2");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 00, 00),
                new DateTime(2025, 01, 16, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Monthly_SkipsTheMonthsWithoutTheStartDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=4");
        var startDate = new DateTime(2025, 01, 31, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 31, 09, 00, 00),
                new DateTime(2025, 03, 31, 09, 00, 00),
                new DateTime(2025, 05, 31, 09, 00, 00),
                new DateTime(2025, 07, 31, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Yearly_SkipsTheYearsWithoutTheStartDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;COUNT=3");
        var startDate = new DateTime(2024, 02, 29, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2024, 02, 29, 09, 00, 00),
                new DateTime(2028, 02, 29, 09, 00, 00),
                new DateTime(2032, 02, 29, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Monthly_ByMonthWithoutADayPartKeepsOnlyTheListedMonths()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTH=1,6;COUNT=3");
        var startDate = new DateTime(2025, 01, 10, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 10, 09, 00, 00),
                new DateTime(2025, 06, 10, 09, 00, 00),
                new DateTime(2026, 01, 10, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Weekly_BySetPosition()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=3");
        var startDate = new DateTime(2025, 01, 13, 09, 00, 00); // Monday

        Assert.Equal(
            [
                new DateTime(2025, 01, 17, 09, 00, 00),
                new DateTime(2025, 01, 24, 09, 00, 00),
                new DateTime(2025, 01, 31, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Weekly_BySetPosition_CountsThePositionInTheWholeWeek()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,FR;BYSETPOS=1;COUNT=2");
        var startDate = new DateTime(2025, 01, 15, 09, 00, 00); // Wednesday

        // The first instance of the week is Monday 13, which is before the start date, so the week produces nothing
        Assert.Equal(
            [
                new DateTime(2025, 01, 20, 09, 00, 00),
                new DateTime(2025, 01, 27, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Daily_BySetPosition_SelectsAmongTheExpandedTimes()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,17;BYSETPOS=1;COUNT=2");
        var startDate = new DateTime(2025, 01, 15, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 00, 00),
                new DateTime(2025, 01, 16, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Hourly_BySetPosition_SelectsWithinTheHour()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;BYMINUTE=0,20,40;BYSETPOS=-1;COUNT=2");
        var startDate = new DateTime(2025, 01, 15, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 15, 09, 40, 00),
                new DateTime(2025, 01, 15, 10, 40, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Monthly_BySetPosition_IsAppliedAfterTheTimeExpansion()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=MO;BYHOUR=9,17;BYSETPOS=-1;COUNT=2");
        var startDate = new DateTime(2025, 01, 01, 00, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 27, 17, 00, 00),
                new DateTime(2025, 02, 24, 17, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=DAILY;BYMONTHDAY=-1;COUNT=3")]
    [InlineData("FREQ=HOURLY;BYMONTHDAY=-1;BYHOUR=9;COUNT=3")]
    [InlineData("FREQ=MINUTELY;BYMONTHDAY=-1;BYHOUR=9;BYMINUTE=0;COUNT=3")]
    [InlineData("FREQ=SECONDLY;BYMONTHDAY=-1;BYHOUR=9;BYMINUTE=0;BYSECOND=0;COUNT=3")]
    public void NegativeByMonthDay_CountsFromTheEndOfTheMonth(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var startDate = new DateTime(2025, 01, 15, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 31, 09, 00, 00),
                new DateTime(2025, 02, 28, 09, 00, 00),
                new DateTime(2025, 03, 31, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,MO;COUNT=2")]
    [InlineData("FREQ=YEARLY;BYDAY=MO,MO;COUNT=2")]
    [InlineData("FREQ=MONTHLY;BYDAY=2MO,2MO,MO;COUNT=2")]
    public void DuplicateByDay_IsTolerated(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var startDate = new DateTime(2025, 01, 01, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 06, 09, 00, 00),
                new DateTime(2025, 01, 13, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=99999999999MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=0MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=54MO")]
    [InlineData("FREQ=YEARLY;BYDAY=-99MO")]
    [InlineData("FREQ=YEARLY;BYDAY=+MO")]
    [InlineData("FREQ=DAILY;COUNT=abc")]
    [InlineData("FREQ=DAILY;COUNT=1.5")]
    [InlineData("FREQ=DAILY;COUNT=")]
    [InlineData("FREQ=DAILY;INTERVAL=abc")]
    [InlineData("FREQ=DAILY;INTERVAL=99999999999")]
    [InlineData("FREQ=DAILY;INTERVAL=1e1")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=abc")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,,2")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=")]
    [InlineData("FREQ=DAILY;BYHOUR=1,x")]
    [InlineData("FREQ=DAILY;BYHOUR=(5)")]
    [InlineData("FREQ=DAILY;BYHOUR= 5")]
    [InlineData("FREQ=DAILY;BYMINUTE=1,")]
    [InlineData("FREQ=DAILY;BYSECOND=0x10")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=1_0")]
    [InlineData("FREQ=YEARLY;BYMONTH=Janvier")]
    [InlineData("FREQ=YEARLY;BYMONTH=January")]
    [InlineData("FREQ=YEARLY;BYMONTH=+1")]
    [InlineData("FREQ=YEARLY;BYMONTH=001")]
    [InlineData("FREQ=DAILY;BYHOUR=+007")]
    [InlineData("FREQ=DAILY;BYHOUR=007")]
    [InlineData("FREQ=DAILY;BYHOUR=+7")]
    [InlineData("FREQ=DAILY;BYHOUR=-0")]
    [InlineData("FREQ=DAILY;BYMINUTE=+0")]
    [InlineData("FREQ=DAILY;BYSECOND=-0")]
    [InlineData("FREQ=DAILY;COUNT=+5")]
    [InlineData("FREQ=DAILY;COUNT=-0")]
    [InlineData("FREQ=DAILY;INTERVAL=+2")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=+0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=001")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=0001")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-001")]
    [InlineData("FREQ=MONTHLY;BYDAY=+001MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=-0MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-0")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=+0001")]
    [InlineData("FREQ=1")]
    [InlineData("FREQ=DAILY,WEEKLY")]
    [InlineData("FREQ=None")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=0")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=367")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-367")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=0")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=54")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYDAY=1MO")]
    [InlineData("FREQ=MONTHLY;BYWEEKNO=1")]
    [InlineData("FREQ=DAILY;BYWEEKNO=1")]
    [InlineData("FREQ=DAILY;BYYEARDAY=1")]
    [InlineData("FREQ=WEEKLY;BYYEARDAY=1")]
    [InlineData("FREQ=MONTHLY;BYYEARDAY=1")]
    [InlineData("FREQ=DAILY;UNTIL=abc")]
    [InlineData("FREQ=DAILY;UNTIL=20250230T000000Z")]
    [InlineData("FREQ=DAILY;WKST=")]
    [InlineData("FREQ=DAILY;BYSECOND=61")]
    [InlineData("FREQ=DAILY;BYSECOND=-1")]
    [InlineData("FREQ=DAILY;BYMINUTE=60")]
    [InlineData("FREQ=DAILY;BYHOUR=24")]
    [InlineData("FREQ=DAILY;BYMONTH=0")]
    [InlineData("FREQ=DAILY;BYMONTH=13")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-32")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=0")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-367")]
    [InlineData("FREQ=DAILY;CONUT=3")]
    [InlineData("FREQ=DAILY; COUNT=3")]
    [InlineData("FREQ=DAILY;COUNT =3")]
    [InlineData("FREQ=DAILY;X-=1")]
    [InlineData("FREQ=DAILY;X-NA ME=1")]
    [InlineData("FREQ=DAILY;X-NAME")]
    [InlineData("FREQ=DAILY;XNAME=1")]
    [InlineData("FREQ=DAILY;RSCALE=HEBREW")]
    [InlineData("FREQ=DAILY;RSCALE=")]
    [InlineData("FREQ=DAILY;RSCALE=GREGORIAN;SKIP=FORWARD")]
    [InlineData("FREQ=DAILY;RSCALE=GREGORIAN;SKIP=BACKWARD")]
    [InlineData("FREQ=DAILY;SKIP=OMIT")]
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=1")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;BYMONTHDAY=-1")]
    [InlineData("FREQ=DAILY;BYSETPOS=1")]
    [InlineData("FREQ=MONTHLY;BYSETPOS=-1;COUNT=3")]
    [InlineData("FREQ=YEARLY;BYSETPOS=1")]
    public void TryParse_RejectsInvalidValues(string rruleText)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out var rrule, out var error));
        Assert.Null(rrule);
        Assert.NotNull(error);
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rruleText));
    }

    [Theory]
    [InlineData("FREQ=daily;COUNT=5")]
    [InlineData("FREQ=DAILY;COUNT=005;INTERVAL=02")]
    [InlineData("FREQ=DAILY;BYHOUR=07,7;BYMINUTE=00;BYSECOND=09")]
    [InlineData("FREQ=YEARLY;BYMONTH=01,12")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=+01,-01,31")]
    [InlineData("FREQ=MONTHLY;BYDAY=+01MO,-05FR,2TU")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=+001,-366;BYWEEKNO=+01,-53;BYSETPOS=+001,-001")]
    [InlineData("FREQ=DAILY;X-NAME=1")]
    [InlineData("FREQ=MONTHLY;BYDAY=+1MO,-5FR")]
    [InlineData("FREQ=YEARLY;BYDAY=53MO,-53SU")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,12")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1,53,-1,-53")]
    [InlineData("FREQ=YEARLY;BYDAY=MO;BYSETPOS=1,366,-1,-366")]
    [InlineData("FREQ=SECONDLY;BYYEARDAY=1")]
    [InlineData("FREQ=DAILY;BYSECOND=0,60;BYMINUTE=0,59;BYHOUR=0,23")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,12;BYMONTHDAY=1,31,-1,-31;BYYEARDAY=1,366,-1,-366")]
    [InlineData("FREQ=DAILY;RSCALE=GREGORIAN")]
    [InlineData("FREQ=MONTHLY;rscale=gregorian;skip=omit")]
    [InlineData("FREQ=MONTHLY;BYHOUR=9;BYSETPOS=1")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;BYSETPOS=-1")]
    public void TryParse_AcceptsValidValues(string rruleText)
    {
        Assert.True(RecurrenceRule.TryParse(rruleText, out _, out var error), error);
    }

    [Theory]
    [InlineData("FREQ=DAILY;CONUT=3", "CONUT")]
    [InlineData("FREQ=DAILY; COUNT=3", " COUNT")]
    [InlineData("FREQ=DAILY;COUNT =3", "COUNT ")]
    public void TryParse_RejectsUnknownRulePartNames(string rruleText, string name)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out _, out var error));
        Assert.Equal($"Unknown rule part: '{name}'.", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyValue_ReportsAnError(string rruleText)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out var rrule, out var error));
        Assert.Null(rrule);
        Assert.Equal("The recurrence rule is empty.", error);
        Assert.False(RecurrenceRule.TryParse(rruleText.AsSpan(), out _, out var spanError));
        Assert.Equal("The recurrence rule is empty.", spanError);
        Assert.Equal($"RRule value '{rruleText}' is invalid: The recurrence rule is empty.", Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rruleText)).Message);
    }

    [Fact]
    public void TryParse_NullValue_ReportsAnError()
    {
        Assert.False(RecurrenceRule.TryParse((string?)null, out var rrule, out var error));
        Assert.Null(rrule);
        Assert.Equal("The recurrence rule is null.", error);
        Assert.Throws<ArgumentNullException>(() => RecurrenceRule.Parse((string)null!));
    }

    [Theory]
    [InlineData("RRULE:FREQ=DAILY")]
    [InlineData("rrule:FREQ=DAILY;COUNT=3")]
    public void TryParse_PropertyName_ReportsThatTheValueMustNotIncludeIt(string rruleText)
    {
        Assert.False(RecurrenceRule.TryParse(rruleText, out _, out var error));
        Assert.Equal("The value must not include the property name: remove the 'RRULE:' prefix.", error);
    }

    [Theory]
    [InlineData("FREQ=SECONDLY;X-FOO=BAR", "FREQ=SECONDLY;X-FOO=BAR")]
    [InlineData("FREQ=MINUTELY;X-FOO=BAR;COUNT=3", "FREQ=MINUTELY;COUNT=3;X-FOO=BAR")]
    [InlineData("FREQ=HOURLY;X-FOO=", "FREQ=HOURLY;X-FOO=")]
    [InlineData("FREQ=DAILY;x-vendor-name=a:b c;X-FOO=1;X-FOO=2", "FREQ=DAILY;x-vendor-name=a:b c;X-FOO=1;X-FOO=2")]
    [InlineData("X-FOO=BAR;FREQ=WEEKLY;BYDAY=MO", "FREQ=WEEKLY;BYDAY=MO;X-FOO=BAR")]
    [InlineData("FREQ=MONTHLY;X-FOO=BAR;BYMONTHDAY=1", "FREQ=MONTHLY;BYMONTHDAY=1;X-FOO=BAR")]
    [InlineData("FREQ=YEARLY;X-FOO=BAR;BYMONTH=1", "FREQ=YEARLY;BYMONTH=1;X-FOO=BAR")]
    public void TryParse_ExtensionRulePart_IsWrittenBackAndIgnoredByTheEvaluation(string rruleText, string expectedText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var withoutExtensions = RecurrenceRule.Parse(string.Join(";", rruleText.Split(';').Where(part => !part.StartsWith("X-", StringComparison.OrdinalIgnoreCase))));
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        Assert.Equal(expectedText, rrule.Text);
        Assert.Equal(expectedText, RecurrenceRule.Parse(rrule.Text).Text);
        Assert.Equal(withoutExtensions.GetNextOccurrences(startDate).Take(5).ToArray(), rrule.GetNextOccurrences(startDate).Take(5).ToArray());
    }

    [Fact]
    public void TryParse_ExtensionRulePartWithAControlCharacter_IsRejected()
    {
        Assert.False(RecurrenceRule.TryParse("FREQ=DAILY;X-FOO=a\r\nDTSTART:20240101", out _, out var error));
        Assert.Equal("X-FOO value contains a control character.", error);
    }

    [Fact]
    public void TryParse_GregorianScaleAndOmittedDates_AreTheDefaultEvaluation()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;RSCALE=GREGORIAN;SKIP=OMIT;BYMONTHDAY=31;COUNT=3");

        Assert.Equal("FREQ=MONTHLY;COUNT=3;BYMONTHDAY=31", rrule.Text);
        AssertOccurrences(rrule.GetNextOccurrences(new DateTime(2025, 01, 01)), new DateTime(2025, 01, 31), new DateTime(2025, 03, 31), new DateTime(2025, 05, 31));
    }

    [Theory]
    [InlineData("FREQ=SECONDLY;BYYEARDAY=-1;BYMONTHDAY=-1;BYSECOND=0;BYSETPOS=-1")]
    [InlineData("FREQ=MINUTELY;BYYEARDAY=-1;BYMONTHDAY=-1;BYSETPOS=-1")]
    [InlineData("FREQ=HOURLY;BYYEARDAY=-1;BYMONTHDAY=-1;BYSETPOS=-1")]
    [InlineData("FREQ=DAILY;BYMONTHDAY=-1;BYSETPOS=-1")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,FR;BYSETPOS=-1")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1;BYDAY=-1FR;BYSETPOS=-1")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-1;BYYEARDAY=-1;BYMONTHDAY=-1;BYSETPOS=-1")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-366;BYDAY=-53MO")]
    public void Text_IsIndependentOfTheCurrentCulture(string rruleText)
    {
        // Cultures such as sv-SE write negative numbers with U+2212 MINUS SIGN, which the RFC 5545 grammar does not accept
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "−";
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal("−1", (-1).ToString(CultureInfo.CurrentCulture));

            var rrule = RecurrenceRule.Parse(rruleText);

            Assert.Equal(rruleText, rrule.Text);
            Assert.Equal(rruleText, rrule.ToString());
            Assert.Equal(rruleText, RecurrenceRule.Parse(rrule.Text).Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void EndDate_CannotBeSetWhenOccurrencesIsSet()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");

        Assert.Throws<InvalidOperationException>(() => rrule.EndDate = new DateTime(2024, 01, 01));
        Assert.Null(rrule.EndDate);
        Assert.Equal("FREQ=DAILY;COUNT=3", rrule.Text);

        rrule.EndDate = null;
        rrule.Occurrences = null;
        rrule.EndDate = new DateTime(2024, 01, 01);
        Assert.Equal("FREQ=DAILY;UNTIL=20240101T000000", rrule.Text);
    }

    [Fact]
    public void Occurrences_CannotBeSetWhenEndDateIsSet()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240101");

        Assert.Throws<InvalidOperationException>(() => rrule.Occurrences = 3);
        Assert.Null(rrule.Occurrences);
        Assert.Equal("FREQ=DAILY;UNTIL=20240101", rrule.Text);

        rrule.Occurrences = null;
        rrule.EndDate = null;
        rrule.Occurrences = 3;
        Assert.Equal("FREQ=DAILY;COUNT=3", rrule.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(9)]
    public void WeekStart_SetterRejectsValuesThatAreNotADayOfWeek(int value)
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;WKST=SU");

        Assert.Throws<ArgumentOutOfRangeException>(() => rrule.WeekStart = (DayOfWeek)value);
        Assert.Equal(DayOfWeek.Sunday, rrule.WeekStart);
        Assert.Equal("FREQ=WEEKLY;WKST=SU", rrule.ToString());
    }

    [Theory]
    [InlineData("FREQ=DAILY", nameof(RecurrenceRule.BySeconds), 61, "BYSECOND=61", "BYSECOND value '61' is invalid. Must be between 0 and 60.")]
    [InlineData("FREQ=DAILY", nameof(RecurrenceRule.ByMinutes), 60, "BYMINUTE=60", "BYMINUTE value '60' is invalid. Must be between 0 and 59.")]
    [InlineData("FREQ=DAILY", nameof(RecurrenceRule.ByHours), 24, "BYHOUR=24", "BYHOUR value '24' is invalid. Must be between 0 and 23.")]
    [InlineData("FREQ=DAILY", nameof(RecurrenceRule.ByMonths), 13, "BYMONTH=13", "BYMONTH value '13' is invalid. Must be between 1 and 12.")]
    [InlineData("FREQ=MONTHLY", nameof(RecurrenceRule.ByMonthDays), 0, "BYMONTHDAY=0", "BYMONTHDAY value '0' is invalid. Must be between 1 and 31 or between -31 and -1.")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO", nameof(RecurrenceRule.BySetPositions), -367, "BYSETPOS=-367", "BYSETPOS value '-367' is invalid. Must be between 1 and 366 or between -366 and -1.")]
    [InlineData("FREQ=WEEKLY", nameof(RecurrenceRule.ByMonthDays), 1, "BYMONTHDAY=1", "BYMONTHDAY cannot be used when FREQ is WEEKLY.")]
    [InlineData("FREQ=MONTHLY", nameof(RecurrenceRule.BySetPositions), 1, "BYSETPOS=1", "BYSETPOS can only be used in conjunction with another BYxxx rule part.")]
    public void GetNextOccurrences_RejectsAnInvalidValueSetAfterParsing(string rruleText, string propertyName, int value, string expectedPart, string expectedError)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        IList<int> values = [value];
        switch (propertyName)
        {
            case nameof(RecurrenceRule.BySeconds):
                rrule.BySeconds = values;
                break;
            case nameof(RecurrenceRule.ByMinutes):
                rrule.ByMinutes = values;
                break;
            case nameof(RecurrenceRule.ByHours):
                rrule.ByHours = values;
                break;
            case nameof(RecurrenceRule.ByMonths):
                rrule.ByMonths = values;
                break;
            case nameof(RecurrenceRule.ByMonthDays):
                rrule.ByMonthDays = values;
                break;
            case nameof(RecurrenceRule.BySetPositions):
                rrule.BySetPositions = values;
                break;
        }

        // The text stays available, for instance in a debugger, even though it cannot be parsed back
        Assert.Contains(expectedPart, rrule.Text);
        Assert.False(RecurrenceRule.TryParse(rrule.Text, out _, out var parseError));
        Assert.Equal(expectedError, parseError);

        var startDate = new DateTime(2024, 01, 01);
        var expectedMessage = "The recurrence rule is invalid: " + expectedError;
        Assert.Equal(expectedMessage, Assert.Throws<InvalidOperationException>(() => rrule.GetNextOccurrences(startDate).ToArray()).Message);
        Assert.Equal(expectedMessage, Assert.Throws<InvalidOperationException>(() => rrule.GetNextOccurrence(startDate)).Message);
        Assert.Equal(expectedMessage, Assert.Throws<InvalidOperationException>(() => rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc).ToArray()).Message);
    }

    [Fact]
    public void GetNextOccurrences_ValidatesAListModifiedInPlace()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
        Assert.NotNull(rrule.ByHours);
        rrule.ByHours.Add(24);

        Assert.Equal("FREQ=DAILY;BYHOUR=9,24", rrule.Text);
        Assert.Throws<InvalidOperationException>(() => rrule.GetNextOccurrences(new DateTime(2024, 01, 01)).First());
    }

    [Fact]
    public void GetNextOccurrences_ModifyingTheCountDoesNotAffectARunningEnumeration()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        var startDate = new DateTime(2024, 01, 01);

        using var enumerator = rrule.GetNextOccurrences(startDate).GetEnumerator();
        using var timeZoneEnumerator = rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.True(timeZoneEnumerator.MoveNext());

        rrule.Occurrences = 1;
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        Assert.Equal(new DateTime(2024, 01, 03), enumerator.Current);
        Assert.False(enumerator.MoveNext());

        rrule.Occurrences = null;
        Assert.True(timeZoneEnumerator.MoveNext());
        Assert.True(timeZoneEnumerator.MoveNext());
        Assert.False(timeZoneEnumerator.MoveNext());

        rrule.Occurrences = 1;
        AssertOccurrences(rrule.GetNextOccurrences(startDate), startDate);
    }

    [Fact]
    public void Yearly_ByWeekNumber_IncludesTheDaysOfWeekOneInThePreviousYear()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;BYDAY=MO;COUNT=4");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        // Week 1 of 2025 starts on Monday, December 30, 2024 and week 1 of 2026 on Monday, December 29, 2025
        Assert.Equal(
            [
                new DateTime(2024, 01, 01, 09, 00, 00),
                new DateTime(2024, 12, 30, 09, 00, 00),
                new DateTime(2025, 12, 29, 09, 00, 00),
                new DateTime(2027, 01, 04, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Yearly_ByWeekNumber_HonorsTheWeekStart()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=-1;BYDAY=SU;WKST=SU;COUNT=3");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2024, 12, 22, 09, 00, 00),
                new DateTime(2025, 12, 28, 09, 00, 00),
                new DateTime(2026, 12, 27, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Yearly_ByWeekNumberWithoutByDayExpandsToTheWholeWeek()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;WKST=SU;COUNT=5");
        var startDate = new DateTime(2025, 01, 01, 09, 00, 00);

        Assert.Equal(
            [
                new DateTime(2025, 01, 01, 09, 00, 00),
                new DateTime(2025, 01, 02, 09, 00, 00),
                new DateTime(2025, 01, 03, 09, 00, 00),
                new DateTime(2025, 01, 04, 09, 00, 00),
                new DateTime(2026, 01, 04, 09, 00, 00),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Yearly_ByWeekNumber_TheIntervalCountsWeekNumberingYears()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=2;BYWEEKNO=1;BYDAY=MO;COUNT=5");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        // Week 1 of 2026 starts on Monday, December 29, 2025 and week 1 of 2030 on Monday, December 31, 2029
        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 01, 01, 09, 00, 00),
            new DateTime(2025, 12, 29, 09, 00, 00),
            new DateTime(2028, 01, 03, 09, 00, 00),
            new DateTime(2029, 12, 31, 09, 00, 00),
            new DateTime(2031, 12, 29, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_BySetPositionSelectsWithinTheWeekNumberingYear()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;BYSETPOS=-1;COUNT=4");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        // December 30 and 31, 2024 are in week 1 of 2025, so they do not end week 1 of 2024
        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 01, 07, 09, 00, 00),
            new DateTime(2025, 01, 05, 09, 00, 00),
            new DateTime(2026, 01, 04, 09, 00, 00),
            new DateTime(2027, 01, 10, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_BySetPositionHonorsTheWeekStart()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;WKST=SU;BYSETPOS=1;COUNT=3");
        var startDate = new DateTime(2024, 01, 01, 09, 00, 00);

        // With weeks starting on Sunday, week 1 of 2024 starts on December 31, 2023, before the start date
        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 12, 29, 09, 00, 00),
            new DateTime(2026, 01, 04, 09, 00, 00),
            new DateTime(2027, 01, 03, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_TheFirstPeriodIsTheWeekNumberingYearOfTheStartDate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=2;BYWEEKNO=1;BYDAY=TU;COUNT=3");
        var startDate = new DateTime(2024, 12, 31, 09, 00, 00);

        // December 31, 2024 is the Tuesday of week 1 of 2025, so the periods are 2025, 2027 and 2029
        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2024, 12, 31, 09, 00, 00),
            new DateTime(2027, 01, 05, 09, 00, 00),
            new DateTime(2029, 01, 02, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_Week53EndsInTheNextCalendarYear()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=53;BYDAY=MO,SU;BYSETPOS=-1;COUNT=2");
        var startDate = new DateTime(2020, 01, 01, 09, 00, 00);

        // Only 2020 and 2026 have a week 53 before 2030, and each one ends on a Sunday in January
        AssertOccurrences(rrule.GetNextOccurrences(startDate),
            new DateTime(2021, 01, 03, 09, 00, 00),
            new DateTime(2027, 01, 03, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_TheOtherDayPartsApplyToTheCalendarDate()
    {
        var startDate = new DateTime(2020, 01, 01, 09, 00, 00);

        // The last week of 2020 ends on January 3, 2021, and the last week of 2022 holds January 1, 2023
        AssertOccurrences(RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=-1;BYMONTH=1;COUNT=6").GetNextOccurrences(startDate),
            new DateTime(2021, 01, 01, 09, 00, 00),
            new DateTime(2021, 01, 02, 09, 00, 00),
            new DateTime(2021, 01, 03, 09, 00, 00),
            new DateTime(2022, 01, 01, 09, 00, 00),
            new DateTime(2022, 01, 02, 09, 00, 00),
            new DateTime(2023, 01, 01, 09, 00, 00));

        // The last day of a calendar year that is in week 1 of the next year
        AssertOccurrences(RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;BYYEARDAY=-1;COUNT=4").GetNextOccurrences(startDate),
            new DateTime(2024, 12, 31, 09, 00, 00),
            new DateTime(2025, 12, 31, 09, 00, 00),
            new DateTime(2029, 12, 31, 09, 00, 00),
            new DateTime(2030, 12, 31, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByWeekNumber_AtTheStartOfTheDateRange()
    {
        var startDate = new DateTime(0001, 01, 01);

        // January 1, 0001 is a Monday: with weeks starting on Thursday, its first days are in the last week of year 0
        AssertOccurrences(RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=-1;WKST=TH;COUNT=5").GetNextOccurrences(startDate),
            new DateTime(0001, 01, 01),
            new DateTime(0001, 01, 02),
            new DateTime(0001, 01, 03),
            new DateTime(0001, 12, 27),
            new DateTime(0001, 12, 28));

        AssertOccurrences(RecurrenceRule.Parse("FREQ=YEARLY;BYWEEKNO=1;WKST=TH;COUNT=2").GetNextOccurrences(startDate),
            new DateTime(0001, 01, 04),
            new DateTime(0001, 01, 05));
    }

    [Fact]
    public void Secondly_ByMonthSkipsTheOtherMonths()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;BYMONTH=12;COUNT=1");
        var startDate = new DateTime(2025, 01, 01, 00, 00, 00);

        Assert.Equal([new DateTime(2025, 12, 01, 00, 00, 00)], rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Fact]
    public void Secondly_ByMonthStaysOnTheIntervalGrid()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;INTERVAL=7;BYMONTH=2;COUNT=2");
        var startDate = new DateTime(2025, 01, 01, 00, 00, 00);

        // 2678400 seconds separate January 1 from February 1; the first multiple of 7 at or after it is 2678403
        Assert.Equal(
            [
                new DateTime(2025, 02, 01, 00, 00, 03),
                new DateTime(2025, 02, 01, 00, 00, 10),
            ],
            rrule.GetNextOccurrences(startDate).ToArray());
    }

    [Theory]
    [InlineData("FREQ=SECONDLY;BYMONTH=2;BYMONTHDAY=30")]
    [InlineData("FREQ=MINUTELY;BYMONTH=2;BYMONTHDAY=30")]
    [InlineData("FREQ=DAILY;BYMONTH=4,6;BYMONTHDAY=31")]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-30")]
    [InlineData("FREQ=MINUTELY;INTERVAL=2;BYMINUTE=1")]
    [InlineData("FREQ=HOURLY;INTERVAL=24;BYHOUR=10")]
    [InlineData("FREQ=SECONDLY;BYMONTH=1,2,3,4,5,6,7,8,9,10,11,12;BYSETPOS=2")]
    [InlineData("FREQ=DAILY;INTERVAL=7;BYDAY=TU")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;BYMONTH=2;BYSETPOS=2")]
    [InlineData("FREQ=HOURLY;INTERVAL=168;BYDAY=TU")]
    [InlineData("FREQ=SECONDLY;INTERVAL=604800;BYDAY=TU")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1;BYSETPOS=2")]
    [InlineData("FREQ=YEARLY;INTERVAL=4;BYMONTH=2;BYMONTHDAY=29")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=366;BYMONTH=1")]
    [InlineData("FREQ=MONTHLY;BYDAY=5MO;BYMONTHDAY=1")]
    public void UnsatisfiableRule_EndsTheEnumeration(string rruleText)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var startDate = new DateTime(2025, 01, 06, 09, 00, 00); // Monday

        Assert.Empty(rrule.GetNextOccurrences(startDate));
    }

    [Theory]
    [InlineData("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29;BYDAY=MO", "2044-02-29T09:00:00", "2072-02-29T09:00:00")]
    [InlineData("FREQ=DAILY;BYMONTH=2;BYMONTHDAY=29;BYDAY=MO", "2044-02-29T09:00:00", "2072-02-29T09:00:00")]
    [InlineData("FREQ=MONTHLY;BYMONTH=2;BYMONTHDAY=29;BYDAY=MO", "2044-02-29T09:00:00", "2072-02-29T09:00:00")]
    public void SparseRule_IsNotEndedBeforeItsNextMatch(string rruleText, string expected1, string expected2)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var startDate = new DateTime(2025, 01, 06, 09, 00, 00);

        AssertOccurrencesStartWith(rrule.GetNextOccurrences(startDate),
            DateTime.Parse(expected1, CultureInfo.InvariantCulture),
            DateTime.Parse(expected2, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SparseRule_WithAnIntervalMatchingTheCalendarCycle_EndsWhenTheStartYearDoesNotMatch()
    {
        // 2100, 2500, 2900, ... are never leap years, so no period of this rule holds February 29
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;INTERVAL=400;BYMONTH=2;BYMONTHDAY=29");

        Assert.Empty(rrule.GetNextOccurrences(new DateTime(2100, 01, 01)));
        AssertOccurrencesStartWith(rrule.GetNextOccurrences(new DateTime(2000, 01, 01)), new DateTime(2000, 02, 29), new DateTime(2400, 02, 29));
    }

    [Fact]
    public void Until_Floating_IsParsedAsAWallClockValue()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240105T100000");

        Assert.Equal(new DateTime(2024, 01, 05, 10, 00, 00), rrule.EndDate);
        Assert.Equal(DateTimeKind.Unspecified, rrule.EndDate?.Kind);
        Assert.Equal("FREQ=DAILY;UNTIL=20240105T100000", rrule.Text);
        AssertOccurrences(rrule.GetNextOccurrences(new DateTime(2024, 01, 04, 10, 00, 00)),
            new DateTime(2024, 01, 04, 10, 00, 00),
            new DateTime(2024, 01, 05, 10, 00, 00));
    }

    [Fact]
    public void Until_Date_IsParsedAsAWallClockValueAndWrittenBackAsADate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240105");

        Assert.Equal(new DateTime(2024, 01, 05), rrule.EndDate);
        Assert.Equal(DateTimeKind.Unspecified, rrule.EndDate?.Kind);
        Assert.Equal("FREQ=DAILY;UNTIL=20240105", rrule.Text);
    }

    [Fact]
    public void Until_Date_SettingTheEndDateWritesADateTime()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240105");
        rrule.EndDate = new DateTime(2024, 01, 06);

        Assert.Equal("FREQ=DAILY;UNTIL=20240106T000000", rrule.Text);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void Until_Date_IncludesTheOccurrencesOfItsDay(DateTimeKind kind)
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 01, 01, 09, 00, 00, kind)).ToArray();

        Assert.Equal(
            [
                new DateTime(2026, 01, 01, 09, 00, 00, kind),
                new DateTime(2026, 01, 02, 09, 00, 00, kind),
                new DateTime(2026, 01, 03, 09, 00, 00, kind),
            ],
            occurrences);
        Assert.All(occurrences, occurrence => Assert.Equal(kind, occurrence.Kind));
    }

    [Fact]
    public void Until_Date_StopsAtTheEndOfItsDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;UNTIL=20260101");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 01, 01, 22, 00, 00));

        Assert.Equal([new DateTime(2026, 01, 01, 22, 00, 00), new DateTime(2026, 01, 01, 23, 00, 00)], occurrences.ToArray());
    }

    [Fact]
    public void Until_Date_WithADateStart_IncludesItsDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 01, 01));

        Assert.Equal([new DateTime(2026, 01, 01), new DateTime(2026, 01, 02), new DateTime(2026, 01, 03)], occurrences.ToArray());
    }

    [Fact]
    public void Until_Date_IncludesItsDayForADateTimeOffsetStart()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");
        var offset = TimeSpan.FromHours(2);

        Assert.Equal(new DateTimeOffset(2026, 01, 03, 09, 00, 00, offset), rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 03, 09, 00, 00, offset)));
        Assert.Null(rrule.GetNextOccurrence(new DateTimeOffset(2026, 01, 04, 00, 00, 00, offset)));
    }

    [Fact]
    public void Until_Date_IncludesItsDayInATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 01, 01, 09, 00, 00), TimeZoneInfo.Utc).ToArray();

        Assert.HasCount(3, occurrences);
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 01, 01, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2026, 01, 02, 09, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2026, 01, 03, 09, 00, 00, TimeSpan.Zero));
    }

    [Fact]
    public void Until_Date_SettingTheEndDateMakesItAnExactBound()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260103");
        rrule.EndDate = new DateTime(2026, 01, 03);

        var occurrences = rrule.GetNextOccurrences(new DateTime(2026, 01, 01, 09, 00, 00));

        Assert.Equal([new DateTime(2026, 01, 01, 09, 00, 00), new DateTime(2026, 01, 02, 09, 00, 00)], occurrences.ToArray());
    }

    [Fact]
    public void Until_Date_AtTheEndOfTheDateRange_IncludesItsDay()
    {
        var rrule = RecurrenceRule.Parse("FREQ=HOURLY;UNTIL=99991231");

        var occurrences = rrule.GetNextOccurrences(new DateTime(9999, 12, 31, 22, 00, 00));

        Assert.Equal([new DateTime(9999, 12, 31, 22, 00, 00), new DateTime(9999, 12, 31, 23, 00, 00)], occurrences.ToArray());
    }

    [Fact]
    public void Until_WithAnOffset_IsParsedAsAUtcValue()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240105T100000+02:00");

        Assert.Equal(new DateTime(2024, 01, 05, 08, 00, 00, DateTimeKind.Utc), rrule.EndDate);
        Assert.Equal(DateTimeKind.Utc, rrule.EndDate?.Kind);
    }

    [Fact]
    public void Until_Utc_BoundsALocalStartDateByInstant()
    {
        var startDate = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc).ToLocalTime();
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=1");
        rrule.Occurrences = null;
        rrule.EndDate = startDate.AddDays(2).ToUniversalTime();

        var occurrences = rrule.GetNextOccurrences(startDate).ToArray();

        Assert.Equal(new[] { startDate, startDate.AddDays(1), startDate.AddDays(2) }, occurrences);
        Assert.All(occurrences, occurrence => Assert.Equal(DateTimeKind.Local, occurrence.Kind));
    }

    [Fact]
    public void Until_Local_BoundsAUtcStartDateByInstant()
    {
        var startDate = new DateTime(2024, 01, 01, 12, 00, 00, DateTimeKind.Utc);
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=1");
        rrule.Occurrences = null;
        rrule.EndDate = startDate.AddDays(2).ToLocalTime();

        var occurrences = rrule.GetNextOccurrences(startDate);

        AssertOccurrences(occurrences, startDate, startDate.AddDays(1), startDate.AddDays(2));
    }

    [Fact]
    public void Until_Utc_IsComparedAsAWallClockForAnUnspecifiedStartDate()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240102T090000Z");

        var occurrences = rrule.GetNextOccurrences(new DateTime(2024, 01, 01, 09, 00, 00));

        AssertOccurrences(occurrences, new DateTime(2024, 01, 01, 09, 00, 00), new DateTime(2024, 01, 02, 09, 00, 00));
    }

    [Fact]
    public void Yearly_ByMonthsAndByMonthDays_SetAfterParsing_AreUsed()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTHDAY=1");
        rrule.ByMonths = [6];
        rrule.ByMonthDays = [15];

        Assert.Equal("FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=15", rrule.Text);
        Assert.Equal("every year on June the 15th", rrule.GetHumanText(CultureInfo.InvariantCulture));
        AssertOccurrencesStartWith(rrule.GetNextOccurrences(new DateTime(2024, 01, 01)), new DateTime(2024, 06, 15), new DateTime(2025, 06, 15));
    }

    [Fact]
    public void Until_EndsTheEnumerationWhenNoPeriodMatches()
    {
        var rrule = RecurrenceRule.Parse("FREQ=SECONDLY;BYDAY=TU;BYMONTHDAY=1;BYMONTH=2;UNTIL=20250301T000000Z");
        var startDate = new DateTime(2025, 01, 01, 00, 00, 00);

        Assert.Empty(rrule.GetNextOccurrences(startDate));
        Assert.Empty(rrule.GetNextOccurrences(startDate, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("FREQ=SECONDLY", 2)]
    [InlineData("FREQ=MINUTELY", 1)]
    [InlineData("FREQ=HOURLY;BYMINUTE=59;BYSECOND=58,59", 2)]
    [InlineData("FREQ=DAILY;BYHOUR=23;BYMINUTE=59;BYSECOND=58,59", 2)]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR,SA,SU", 1)]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1", 1)]
    [InlineData("FREQ=YEARLY;BYYEARDAY=-1", 1)]
    [InlineData("FREQ=YEARLY;BYWEEKNO=-1", 1)]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1;WKST=FR", 1)]
    [InlineData("FREQ=YEARLY;BYWEEKNO=1", 0)]
    public void IteratingPastTheMaximumDate_EndsTheEnumeration(string rruleText, int expectedCount)
    {
        var rrule = RecurrenceRule.Parse(rruleText);
        var startDate = new DateTime(9999, 12, 31, 23, 59, 58);

        Assert.HasCount(expectedCount, rrule.GetNextOccurrences(startDate));
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
