namespace Meziantou.Framework.Scheduling.Tests;

public sealed class InternetCalendarTests
{
    // A value that contains an escaped "\nBEGIN:VEVENT" still holds those characters, so the
    // injection has to be judged on the content lines rather than on a substring search.
    private static int CountContentLines(string ics, string line)
    {
        var count = 0;
        foreach (var contentLine in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(contentLine, line, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string GetContentLine(string ics, string name)
    {
        foreach (var contentLine in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (contentLine.StartsWith(name + ":", StringComparison.Ordinal))
            {
                return contentLine;
            }
        }

        Assert.Fail($"No '{name}' content line in:\n{ics}");
        throw new UnreachableException();
    }

    private static InternetCalendar CreateCalendarWithEvent(Event @event)
    {
        var calendar = new InternetCalendar();
        calendar.Events.Add(@event);
        return calendar;
    }

    private static Event CreateEvent()
    {
        return new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Utc),
        };
    }

    [Fact]
    public void ToIcs_SeparatesContentLinesWithCrLf()
    {
        var calendar = CreateCalendarWithEvent(CreateEvent());

        var ics = calendar.ToIcs();

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.DoesNotContain("\n\r", ics);
    }

    [Fact]
    public void ToIcs_ANewLineInTheSummaryDoesNotInjectAProperty()
    {
        var @event = CreateEvent();
        @event.Summary = "Hi\r\nATTENDEE:MAILTO:evil@example.com\r\nX-EVIL:1";
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Contains("SUMMARY:Hi\\nATTENDEE:MAILTO:evil@example.com\\nX-EVIL:1\r\n", ics);
        Assert.DoesNotContain("\r\nATTENDEE:", ics);
        Assert.DoesNotContain("\r\nX-EVIL:", ics);
    }

    [Fact]
    public void ToIcs_EscapesTheStructuralCharactersOfATextValue()
    {
        var @event = CreateEvent();
        @event.Summary = @"Lunch; with Bob, Jr \ friends";
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Contains(@"SUMMARY:Lunch\; with Bob\, Jr \\ friends" + "\r\n", ics);
    }

    [Fact]
    public void ToIcs_EscapesTheEventIdentifier()
    {
        var @event = CreateEvent();
        @event.Id = "uid\r\nBEGIN:VEVENT";
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Contains("UID:uid\\nBEGIN:VEVENT\r\n", ics);
        var beginEventCount = CountContentLines(ics, "BEGIN:VEVENT");
        Assert.Equal(1, beginEventCount);
    }

    [Fact]
    public void ToIcs_ANewLineInAnAdditionalPropertyDoesNotCloseTheCalendar()
    {
        var calendar = new InternetCalendar();
        calendar.AdditionalProperties["X-A"] = "v\r\nEND:VCALENDAR\r\nBEGIN:VCALENDAR";

        var ics = calendar.ToIcs();

        Assert.Contains("X-A:v\\nEND:VCALENDAR\\nBEGIN:VCALENDAR\r\n", ics);
        var beginCalendarCount = CountContentLines(ics, "BEGIN:VCALENDAR");
        var endCalendarCount = CountContentLines(ics, "END:VCALENDAR");
        Assert.Equal(1, beginCalendarCount);
        Assert.Equal(1, endCalendarCount);
    }

    [Theory]
    [InlineData("X-A\r\nEND:VCALENDAR")]
    [InlineData("X-A:INJECTED")]
    [InlineData("X A")]
    [InlineData("")]
    public void ToIcs_SkipsAnAdditionalPropertyWhoseNameIsNotAValidPropertyName(string name)
    {
        var calendar = new InternetCalendar();
        calendar.AdditionalProperties[name] = "value";

        var ics = calendar.ToIcs();

        Assert.DoesNotContain("value", ics);
    }

    [Theory]
    [InlineData("X-MICROSOFT-CDO-BUSYSTATUS")]
    [InlineData("X-CUSTOM-1")]
    public void ToIcs_KeepsAnAdditionalPropertyWhoseNameIsValid(string name)
    {
        var calendar = new InternetCalendar();
        calendar.AdditionalProperties[name] = "OOF";

        var ics = calendar.ToIcs();

        Assert.Contains(name + ":OOF\r\n", ics);
    }

    [Fact]
    public void ToIcs_WritesTheRequiredProductIdentifier()
    {
        var calendar = CreateCalendarWithEvent(CreateEvent());

        var ics = calendar.ToIcs();

        Assert.Contains("PRODID:-//Meziantou//Meziantou.Framework.Scheduling//EN\r\n", ics);
    }

    [Fact]
    public void ToIcs_WritesAUtcDateTimeWithTheZSuffix()
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Utc),
        };
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Equal("DTSTART:20240102T080000Z", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND:20240102T090000Z", GetContentLine(ics, "DTEND"));
    }

    [Fact]
    public void ToIcs_WritesAnUnspecifiedDateTimeAsAFloatingDateTime()
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified),
        };
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Equal("DTSTART:20240102T080000", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND:20240102T090000", GetContentLine(ics, "DTEND"));
    }

    [Fact]
    public void ToIcs_WritesALocalDateTimeAsUtc()
    {
        var start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Local);
        var @event = new Event
        {
            Start = start,
            End = start.AddHours(1),
        };
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        Assert.Equal("DTSTART:" + start.ToUniversalTime().ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture) + "Z", GetContentLine(ics, "DTSTART"));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToIcs_NeverWritesADateTimeWithAUtcOffset(DateTimeKind kind)
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, kind),
            End = new DateTime(2024, 01, 02, 09, 00, 00, kind),
        };
        var calendar = CreateCalendarWithEvent(@event);

        var ics = calendar.ToIcs();

        foreach (var name in new[] { "DTSTART", "DTEND", "CREATED", "LAST-MODIFIED", "DTSTAMP" })
        {
            var value = GetContentLine(ics, name)[(name.Length + 1)..];
            Assert.Matches(@"^\d{8}T\d{6}Z?$", value);
        }
    }
}
