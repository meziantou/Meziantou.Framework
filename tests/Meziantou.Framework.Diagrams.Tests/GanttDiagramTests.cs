using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class GanttDiagramTests
{
    [Fact]
    public void GanttDiagram()
    {
        #region Diagrams_GanttDiagram
        var diagram = new GanttDiagram
        {
            Title = "Release plan",
            AxisFormat = "%b %d",
            TickInterval = new GanttTickInterval(1, GanttTimeUnit.Week),
        };
        diagram.Excludes.Add("weekends");

        var design = diagram.AddSection("Design");
        var specification = design.AddTask("Specification");
        specification.Id = "spec";
        specification.Start = new DateTime(2024, 3, 4);
        specification.Duration = new GanttDuration(5, GanttTimeUnit.Day);
        specification.IsDone = true;
        var review = design.AddTask("Review");
        review.Id = "review";
        review.After.Add("spec");
        review.Duration = new GanttDuration(2, GanttTimeUnit.Day);
        review.IsActive = true;

        var development = diagram.AddSection("Development");
        var api = development.AddTask("API");
        api.Id = "api";
        api.After.Add("review");
        api.Duration = new GanttDuration(2, GanttTimeUnit.Week);
        api.IsCritical = true;
        var client = development.AddTask("Client");
        client.Start = new DateTime(2024, 3, 18);
        client.End = new DateTime(2024, 3, 29);

        var release = development.AddTask("Release");
        release.Id = "release";
        release.After.Add("api");
        release.Duration = new GanttDuration(0, GanttTimeUnit.Day);
        release.IsMilestone = true;
        #endregion

        Assert.Same(review, diagram.Find("review"));
        Assert.Equal(["Specification", "Review", "API", "Client", "Release"], diagram.EnumerateTasks().Select(task => task.Name.Text));
        DiagramAssert.Svg(diagram, "GanttDiagram");
    }

    [Fact]
    public void Schedule()
    {
        var diagram = new GanttDiagram();
        var first = diagram.AddTask("First");
        first.Id = "a";
        first.Start = new DateTime(2024, 1, 1);
        first.Duration = new GanttDuration(3, GanttTimeUnit.Day);
        var second = diagram.AddTask("Second");
        second.Duration = new GanttDuration(12, GanttTimeUnit.Hour);
        var third = diagram.AddTask("Third");
        third.Id = "c";
        third.After.Add("a");
        third.After.Add("task1");
        third.End = new DateTime(2024, 1, 10);
        var fourth = diagram.AddTask("Fourth");
        fourth.Start = new DateTime(2023, 12, 30);
        fourth.Until.Add("c");
        var month = diagram.AddTask("Month");
        month.Start = new DateTime(2024, 1, 31);
        month.Duration = new GanttDuration(1.5, GanttTimeUnit.Month);

        var schedule = GanttSchedule.Compute(diagram);
        Assert.Equal(
            [
                (new DateTime(2024, 1, 1), new DateTime(2024, 1, 4)),
                (new DateTime(2024, 1, 4), new DateTime(2024, 1, 4, 12, 0, 0)),
                (new DateTime(2024, 1, 4, 12, 0, 0), new DateTime(2024, 1, 10)),
                (new DateTime(2023, 12, 30), new DateTime(2024, 1, 4, 12, 0, 0)),
                (new DateTime(2024, 1, 31), new DateTime(2024, 2, 29)),
            ],
            schedule.Select(task => (task.Start, task.End)));
    }

    [Fact]
    public void ScheduleWithExclusionsAndInclusiveEndDates()
    {
        var diagram = new GanttDiagram { InclusiveEndDates = true };
        diagram.Excludes.Add("weekends");
        diagram.Excludes.Add("2024-01-10");
        var extended = diagram.AddTask("Extended");
        extended.Start = new DateTime(2024, 1, 4);
        extended.Duration = new GanttDuration(3, GanttTimeUnit.Day);
        var inclusive = diagram.AddTask("Inclusive");
        inclusive.Start = new DateTime(2024, 1, 8);
        inclusive.End = new DateTime(2024, 1, 9);

        var schedule = GanttSchedule.Compute(diagram);

        // Thursday, Friday, then the weekend is skipped: the task ends on Tuesday
        Assert.Equal((new DateTime(2024, 1, 4), new DateTime(2024, 1, 9)), (schedule[0].Start, schedule[0].End));

        // A manual end date is not extended, but the inclusive end adds a day
        Assert.Equal((new DateTime(2024, 1, 8), new DateTime(2024, 1, 10)), (schedule[1].Start, schedule[1].End));
    }

    [Fact]
    public void Cycles()
    {
        var diagram = new GanttDiagram();
        var a = diagram.AddTask("A");
        a.Id = "a";
        a.After.Add("b");
        a.Duration = new GanttDuration(1, GanttTimeUnit.Day);
        var b = diagram.AddTask("B");
        b.Id = "b";
        b.After.Add("a");
        b.Duration = new GanttDuration(1, GanttTimeUnit.Day);

        var schedule = GanttSchedule.Compute(diagram);
        Assert.All(schedule, task => Assert.Equal(new DateTime(1970, 1, 1), task.Start));
        Assert.DoesNotContain("NaN", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("YYYY-MM-DD", "2024-03-05")]
    [InlineData("YYYY-MM-DD HH:mm:ss", "2024-03-05 14:07:09")]
    [InlineData("DD/MM/YY", "05/03/24")]
    [InlineData("D MMM YYYY", "5 Mar 2024")]
    [InlineData("dddd, MMMM Do", "Tuesday, March 5th")]
    [InlineData("h:mm A", "2:07 PM")]
    [InlineData("[Week] Q YYYY", "Week 1 2024")]
    [InlineData("X", "1709647629")]
    [InlineData("x", "1709647629000")]
    public void FormatDates(string format, string expected)
    {
        Assert.Equal(expected, new GanttDateFormat(format).Format(new DateTime(2024, 3, 5, 14, 7, 9)));
    }

    [Theory]
    [InlineData("YYYY-MM-DD", "2024-02-29", "2024-02-29T00:00:00")]
    [InlineData("YYYY-MM-DD", "2024-01-06T10:30", "2024-01-06T10:30:00")]
    [InlineData("DD/MM/YYYY HH:mm", "05/03/2024 14:07", "2024-03-05T14:07:00")]
    [InlineData("HH:mm", "17:49", "1970-01-01T17:49:00")]
    [InlineData("YYYY", "1913", "1913-01-01T00:00:00")]
    [InlineData("X", "71", "1970-01-01T00:01:11")]
    [InlineData("hh:mm a", "02:07 pm", "1970-01-01T14:07:00")]
    public void ParseDates(string format, string text, string expected)
    {
        var diagram = new GanttDiagram { DateFormat = format };
        Assert.True(diagram.TryParseDate(text, out var date));
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
    }

    [Theory]
    [InlineData("YYYY-MM-DD", "2023-02-29")]
    [InlineData("YYYY-MM-DD", "2024-1-5")]
    [InlineData("YYYY-MM-DD", "after a")]
    [InlineData("DD/MM/YYYY", "32/01/2024")]
    [InlineData("HH:mm", "25:00")]
    public void InvalidDates(string format, string text)
    {
        var diagram = new GanttDiagram { DateFormat = format };
        Assert.False(diagram.TryParseDate(text, out _));
    }

    [Fact]
    public void DatesThatTheFormatCannotRepresentAreWrittenAsIso()
    {
        // The format has no time: the time would be lost
        var diagram = new GanttDiagram();
        Assert.Equal("2024-03-05", diagram.FormatDate(new DateTime(2024, 3, 5)));
        Assert.Equal("2024-03-05T14:07:09.000", diagram.FormatDate(new DateTime(2024, 3, 5, 14, 7, 9)));

        // The day of the week alone does not parse back
        diagram.DateFormat = "dddd";
        Assert.Equal("2024-03-05T00:00:00.000", diagram.FormatDate(new DateTime(2024, 3, 5)));
    }

    [Theory]
    [InlineData("%Y-%m-%d", "2024-03-05")]
    [InlineData("%b %e", "Mar  5")]
    [InlineData("%a %A %B", "Tue Tuesday March")]
    [InlineData("%H:%M:%S.%L", "14:07:09.000")]
    [InlineData("%I %p %j", "02 PM 065")]
    [InlineData("%-d/%-m %y %%", "5/3 24 %")]
    [InlineData("%U %W %V %u %w", "09 10 10 2 2")]
    public void FormatAxisLabels(string format, string expected)
    {
        Assert.Equal(expected, GanttTimeFormat.Format(format, new DateTime(2024, 3, 5, 14, 7, 9)));
    }

    [Fact]
    public void AxisTicks()
    {
        // As d3, about ten ticks: every other day for a month, every week (on Sunday) for two months
        var days = GanttTimeAxis.GetTicks(new DateTime(2024, 1, 1), new DateTime(2024, 1, 20), interval: null, DayOfWeek.Sunday);
        Assert.Equal([1, 3, 5, 7, 9, 11, 13, 15, 17, 19], days.Select(day => day.Day));
        var weeks = GanttTimeAxis.GetTicks(new DateTime(2024, 1, 1), new DateTime(2024, 3, 1), interval: null, DayOfWeek.Monday);
        Assert.Equal([7, 14, 21, 28, 4, 11, 18, 25], weeks.Select(day => day.Day));

        var mondays = GanttTimeAxis.GetTicks(new DateTime(2024, 1, 1), new DateTime(2024, 1, 20), new GanttTickInterval(1, GanttTimeUnit.Week), DayOfWeek.Monday);
        Assert.Equal([new DateTime(2024, 1, 1), new DateTime(2024, 1, 8), new DateTime(2024, 1, 15)], mondays);

        var months = GanttTimeAxis.GetTicks(new DateTime(2024, 1, 15), new DateTime(2024, 12, 1), new GanttTickInterval(3, GanttTimeUnit.Month), DayOfWeek.Sunday);
        Assert.Equal([new DateTime(2024, 4, 1), new DateTime(2024, 7, 1), new DateTime(2024, 10, 1)], months);
    }

    [Fact]
    public void CompactModeTopAxisAndToday()
    {
        var diagram = new GanttDiagram
        {
            Compact = true,
            TopAxis = true,
            Today = new DateTime(2024, 1, 12),
            TodayMarkerStyle = new DiagramStyle { Stroke = (SceneColor)"#00ff00", StrokeWidth = 5 },
            Width = 800,
        };
        var section = diagram.AddSection("Work");
        foreach (var (name, start, days) in new[] { ("A", 1, 5), ("B", 8, 5), ("C", 3, 10) })
        {
            var task = section.AddTask(name);
            task.Start = new DateTime(2024, 1, start);
            task.Duration = new GanttDuration(days, GanttTimeUnit.Day);
        }

        var vertical = section.AddTask("Freeze");
        vertical.Start = new DateTime(2024, 1, 10);
        vertical.Duration = new GanttDuration(0, GanttTimeUnit.Day);
        vertical.IsVertical = true;

        var schedule = GanttSchedule.Compute(diagram);
        Assert.Equal(3, schedule.Count(task => !task.Task.IsVertical));
        var svg = diagram.ToSvg();
        Assert.Contains("stroke=\"#00ff00\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);

        diagram.HideTodayMarker = true;
        Assert.DoesNotContain("stroke=\"#00ff00\"", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void Themes()
    {
        var diagram = new GanttDiagram { Theme = DiagramTheme.Dark };
        var done = diagram.AddTask("Done");
        done.Start = new DateTime(2024, 1, 1);
        done.Duration = new GanttDuration(2, GanttTimeUnit.Day);
        done.IsDone = true;
        var active = diagram.AddTask("Active");
        active.Duration = new GanttDuration(2, GanttTimeUnit.Day);
        active.IsActive = true;
        var critical = diagram.AddTask("Critical");
        critical.Duration = new GanttDuration(2, GanttTimeUnit.Day);
        critical.IsCritical = true;
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void Validation()
    {
        var diagram = new GanttDiagram();
        Assert.Throws<ArgumentException>("value", () => diagram.DateFormat = "YYYY,MM");
        Assert.Throws<ArgumentException>("value", () => diagram.DateFormat = " YYYY");
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.WeekendStart = DayOfWeek.Monday);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.Width = 10);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new GanttDuration(-1, GanttTimeUnit.Day));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new GanttDuration(double.NaN, GanttTimeUnit.Day));
        Assert.Throws<ArgumentOutOfRangeException>("count", () => new GanttTickInterval(0, GanttTimeUnit.Day));
        Assert.Throws<ArgumentOutOfRangeException>("unit", () => new GanttTickInterval(1, GanttTimeUnit.Year));
    }

    [Fact]
    public void EmptyDiagram()
    {
        var svg = new GanttDiagram { Title = "Empty" }.ToSvg();
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
    }
}
