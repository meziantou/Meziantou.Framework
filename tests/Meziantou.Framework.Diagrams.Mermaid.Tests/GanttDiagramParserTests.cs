namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class GanttDiagramParserTests
{
    private static GanttDiagram Parse(string text) => (GanttDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void SectionsAndTasks()
    {
        #region Mermaid_GanttDiagram
        var diagram = (GanttDiagram)MermaidDocument.Parse("""
            gantt
                title A Gantt Diagram
                dateFormat YYYY-MM-DD
                excludes weekends
                section Section
                    A task       :done, a1, 2014-01-01, 30d
                    Another task :after a1, 20d
                section Another
                    Task in sec  :crit, 2014-01-12, 12d
                    Release      :milestone, 2014-02-20, 0d
            """).Diagram;
        #endregion

        Assert.Equal("A Gantt Diagram", diagram.Title);
        Assert.Equal(["weekends"], diagram.Excludes);
        Assert.Equal(["Section", "Another"], diagram.Sections.Select(section => section.Title.Text));
        var first = diagram.Sections[0].Tasks[0];
        Assert.Equal(("a1", new DateTime(2014, 1, 1), new GanttDuration(30, GanttTimeUnit.Day), true), (first.Id, first.Start, first.Duration, first.IsDone));
        var second = diagram.Sections[0].Tasks[1];
        Assert.Equal((null, ["a1"], new GanttDuration(20, GanttTimeUnit.Day)), (second.Id, second.After.ToArray(), second.Duration));
        Assert.True(diagram.Sections[1].Tasks[0].IsCritical);
        Assert.True(diagram.Sections[1].Tasks[1].IsMilestone);
    }

    [Theory]
    [InlineData("1d", 1, GanttTimeUnit.Day)]
    [InlineData("2.5h", 2.5, GanttTimeUnit.Hour)]
    [InlineData("30m", 30, GanttTimeUnit.Minute)]
    [InlineData("3M", 3, GanttTimeUnit.Month)]
    [InlineData("10s", 10, GanttTimeUnit.Second)]
    [InlineData("250ms", 250, GanttTimeUnit.Millisecond)]
    [InlineData("2w", 2, GanttTimeUnit.Week)]
    [InlineData("1y", 1, GanttTimeUnit.Year)]
    public void Durations(string text, double value, GanttTimeUnit unit)
    {
        var task = Parse("gantt\nTask : 2024-01-01, " + text).Tasks[0];
        Assert.Equal(new GanttDuration(value, unit), task.Duration);
    }

    [Fact]
    public void TaskForms()
    {
        var diagram = Parse("""
            gantt
                apple  :a, 2017-07-20, 1w
                banana :crit, b, 2017-07-23, 1d
                cherry :active, c, after b a, 1d
                kiwi   :d, 2017-07-20, until b c
                lemon  :2017-07-22, 2017-07-25
                melon  :1d
                Freeze :vert, v, 2017-07-21, 0d
            """);
        var tasks = diagram.Tasks;
        Assert.Equal(["b", "a"], tasks[2].After);
        Assert.True(tasks[2].IsActive);
        Assert.Equal(["b", "c"], tasks[3].Until);
        Assert.Equal((new DateTime(2017, 7, 22), new DateTime(2017, 7, 25)), (tasks[4].Start, tasks[4].End));
        Assert.Equal((null, 0, new GanttDuration(1, GanttTimeUnit.Day)), (tasks[5].Start, tasks[5].After.Count, tasks[5].Duration));
        Assert.True(tasks[6].IsVertical);
    }

    [Fact]
    public void Settings()
    {
        var diagram = Parse("""
            ---
            displayMode: compact
            config:
              gantt:
                barHeight: 30
                useWidth: 800
            ---
            gantt
                DateFormat DD/MM/YYYY HH:mm
                axisFormat %d %b
                tickInterval 2day
                weekday monday
                weekend friday
                excludes Saturday, 2024-01-10
                includes 2024-01-13
                inclusiveEndDates
                topAxis
                todayMarker stroke-width:5px,stroke:#0f0,opacity:0.5
                accTitle: Accessible
                accDescription The description
                Task : 01/01/2024 10:00, 2d
                click task1 href "https://example.com/"
            """);
        Assert.Equal(("DD/MM/YYYY HH:mm", "%d %b", new GanttTickInterval(2, GanttTimeUnit.Day)), (diagram.DateFormat, diagram.AxisFormat, diagram.TickInterval));
        Assert.Equal((DayOfWeek.Monday, DayOfWeek.Friday), (diagram.WeekStart, diagram.WeekendStart));
        Assert.Equal(["saturday", "2024-01-10"], diagram.Excludes);
        Assert.Equal(["2024-01-13"], diagram.Includes);
        Assert.True(diagram.InclusiveEndDates);
        Assert.True(diagram.TopAxis);
        Assert.True(diagram.Compact);
        Assert.Equal((30, 800), (diagram.BarHeight, diagram.Width));
        Assert.Equal((SceneColor)"#00ff00", diagram.TodayMarkerStyle?.Stroke);
        Assert.Equal(("Accessible", "The description"), (diagram.AccessibleTitle, diagram.Description));
        Assert.Equal(new DateTime(2024, 1, 1, 10, 0, 0), diagram.Tasks[0].Start);
        Assert.Equal("https://example.com/", diagram.Tasks[0].Link);
        Assert.True(Parse("gantt\ntodayMarker off").HideTodayMarker);
    }

    [Fact]
    public void Warnings()
    {
        var document = MermaidDocument.Parse("""
            gantt
                tickInterval 1decade
                A : a, 2024-01-01, soon
                click a call callback("x")
                click a href "javascript:alert(1)"
                click missing href "https://example.com"
            """);
        Assert.Collection(
            document.Warnings,
            warning => Assert.Contains("'1decade' is not supported", warning.Message, StringComparison.Ordinal),
            warning => Assert.Contains("'soon' is neither a date", warning.Message, StringComparison.Ordinal),
            warning => Assert.Contains("callbacks are not supported", warning.Message, StringComparison.Ordinal),
            warning => Assert.Contains("is not allowed", warning.Message, StringComparison.Ordinal),
            warning => Assert.Contains("'missing' is not declared", warning.Message, StringComparison.Ordinal));
        var task = ((GanttDiagram)document.Diagram).Tasks[0];
        Assert.Equal((new GanttDuration(0, GanttTimeUnit.Day), null), (task.Duration, task.Link));
    }

    [Fact]
    public void CommentsAndEntities()
    {
        var diagram = Parse("""
            gantt
                %% A comment
                section Phase #35;1
                Fish#58; salmon #amp; chips : 2024-01-01, 1d %% trailing comment
            """);
        Assert.Equal("Phase #1", diagram.Sections[0].Title.Text);
        Assert.Equal("Fish: salmon & chips", diagram.Sections[0].Tasks[0].Name.Text);
    }

    [Theory]
    [InlineData("gantt\nTask : 1d", "The first task needs a start date.")]
    [InlineData("gantt\nTask", "a task is a name, ':' and its data.")]
    [InlineData("gantt\nTask : a, b, c, d", "A task has an identifier, a start and an end at most.")]
    [InlineData("gantt\nTask : 2024-13-01, 1d", "is not a date in the format 'YYYY-MM-DD'")]
    [InlineData("gantt\n : 2024-01-01, 1d", "The name of the task is expected.")]
    [InlineData("gantt\ndateFormat YYYY, MM", "The date format cannot contain a comma")]
    [InlineData("gantt\nweekend monday", "The weekend starts on friday or saturday.")]
    [InlineData("gantt\nclick a href javascript", "The address is quoted")]
    [InlineData("gantt\ntopAxis extra", "a task is a name, ':' and its data.")]
    public void InvalidGanttDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new GanttDiagram
        {
            Title = "Plan",
            DateFormat = "DD/MM/YYYY",
            AxisFormat = "%d/%m",
            TickInterval = new GanttTickInterval(1, GanttTimeUnit.Week),
            WeekStart = DayOfWeek.Monday,
            WeekendStart = DayOfWeek.Friday,
            InclusiveEndDates = true,
            TopAxis = true,
            Compact = true,
            TodayMarkerStyle = new DiagramStyle { Stroke = (SceneColor)"#ff0000" },
            Width = 900,
            Theme = DiagramTheme.Forest,
        };
        diagram.Excludes.Add("Weekends");
        diagram.Includes.Add("2024-01-13");
        var first = diagram.AddTask("title: the first task");
        first.Id = "first";
        first.Start = new DateTime(2024, 1, 2);
        first.End = new DateTime(2024, 1, 5, 12, 0, 0);
        first.IsDone = true;
        first.Link = "https://example.com/first";
        var section = diagram.AddSection(" Section\nwith two lines ");
        var second = section.AddTask("Second #1");
        second.Id = "second";
        second.Duration = new GanttDuration(1.5, GanttTimeUnit.Day);
        second.IsCritical = true;
        var third = section.AddTask("Third");
        third.After.Add("first");
        third.After.Add("second");
        third.Until.Add("second");
        third.IsMilestone = true;
        section.AddTask("Fourth");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (GanttDiagram)parsed.Diagram;
        Assert.Equal(("Plan", "DD/MM/YYYY", "%d/%m", new GanttTickInterval(1, GanttTimeUnit.Week)), (result.Title, result.DateFormat, result.AxisFormat, result.TickInterval));
        Assert.Equal((DayOfWeek.Monday, DayOfWeek.Friday, true, true, true, 900.0), (result.WeekStart, result.WeekendStart, result.InclusiveEndDates, result.TopAxis, result.Compact, result.Width));
        Assert.Equal(["weekends"], result.Excludes);
        Assert.Same(DiagramTheme.Forest, result.Theme);
        Assert.Equal((SceneColor)"#ff0000", result.TodayMarkerStyle?.Stroke);
        var resultFirst = result.Tasks[0];
        Assert.Equal(("title: the first task", new DateTime(2024, 1, 2), new DateTime(2024, 1, 5, 12, 0, 0), true, "https://example.com/first"), (resultFirst.Name.Text, resultFirst.Start, resultFirst.End, resultFirst.IsDone, resultFirst.Link));
        Assert.Equal(" Section\nwith two lines ", result.Sections[0].Title.Text);
        var tasks = result.Sections[0].Tasks;
        Assert.Equal(("Second #1", new GanttDuration(1.5, GanttTimeUnit.Day), true), (tasks[0].Name.Text, tasks[0].Duration, tasks[0].IsCritical));

        // A task with an identifier and no start starts after the previous task
        Assert.Equal(["first"], tasks[0].After);
        Assert.Equal((["first", "second"], ["second"], true), (tasks[1].After.ToArray(), tasks[1].Until.ToArray(), tasks[1].IsMilestone));
        Assert.Equal(new GanttDuration(0, GanttTimeUnit.Day), tasks[2].Duration);

        Assert.Equal("""
            ---
            config:
              theme: forest
              gantt:
                displayMode: compact
                useWidth: 900
            ---
            gantt
                title Plan
                dateFormat DD/MM/YYYY
                axisFormat %d/%m
                tickInterval 1week
                weekday monday
                weekend friday
                excludes weekends
                includes 2024-01-13
                inclusiveEndDates
                topAxis
                todayMarker stroke:#ff0000
                #116;itle#58; the first task : done, first, 02/01/2024, 2024-01-05T12:00:00.000
                click first href "https://example.com/first"
                section #32;Section<br>with two lines#32;
                Second #35;1 : crit, second, after first, 1.5d
                Third : milestone, after first second, until second
                Fourth : 0d

            """, text);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var diagram = new GanttDiagram();
        var task = diagram.AddTask("Task");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));

        task.Start = new DateTime(2024, 1, 1);
        task.Id = "a,b";
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));

        task.Id = "not valid";
        task.Link = "https://example.com";
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        task.Link = null;

        task.Id = "done";
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));

        task.Id = null;
        diagram.Excludes.Add("two words");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
