using System.Globalization;
using FluentAssertions;
using Xunit;

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

    [Fact]
    public void Daily_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");

        var text = rrule.Text;

        text.Should().Be("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");
    }

    [Fact]
    public void Daily_Text02()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;INTERVAL=2");

        var text = rrule.Text;

        text.Should().Be("FREQ=DAILY;INTERVAL=2;UNTIL=20000131T140000Z");
    }

    [Fact]
    public void Weekly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE");

        var text = rrule.Text;

        text.Should().Be("FREQ=WEEKLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE");
    }

    [Fact]
    public void Monthly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

        var text = rrule.Text;

        text.Should().Be("FREQ=MONTHLY;UNTIL=20000131T140000Z;BYMONTH=1;BYMONTHDAY=2;BYDAY=TU,WE");
    }

    [Fact]
    public void Yearly_Text01()
    {
        var rrule = RecurrenceRule.Parse("FREQ=YEARLY;UNTIL=20000131T140000Z;BYYEARDAY=1,-1;BYMONTH=1;BYDAY=TU,WE;BYMONTHDAY=2");

        var text = rrule.Text;

        text.Should().Be("FREQ=YEARLY;UNTIL=20000131T140000Z;BYMONTH=1;BYYEARDAY=1,-1;BYMONTHDAY=2;BYDAY=TU,WE");
    }

    private static void TestGetHumanText(string rruleText, string cultureInfo, string expectedText)
    {
#if InvariantGlobalization
        var culture = cultureInfo is "en-US" or "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureInfo);
#else
        var culture = CultureInfo.GetCultureInfo(cultureInfo);
#endif

        var rrule = RecurrenceRule.Parse(rruleText);
        var text = rrule.GetHumanText(culture);
        text.Should().Be(expectedText);
    }
}
