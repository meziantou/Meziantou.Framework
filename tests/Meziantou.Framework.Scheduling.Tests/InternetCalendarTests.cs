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
        // A VTIMEZONE component has DTSTART and RRULE properties of its own, so only the event is searched.
        foreach (var contentLine in GetEventContentLines(ics))
        {
            // A property may carry parameters, as in "DTSTART;TZID=...:20240102T080000"
            if (contentLine.StartsWith(name, StringComparison.Ordinal) &&
                contentLine.Length > name.Length &&
                contentLine[name.Length] is ':' or ';')
            {
                return contentLine;
            }
        }

        Assert.Fail($"No '{name}' content line in:\n{ics}");
        throw new System.Diagnostics.UnreachableException();
    }

    private static string GetContentLineValue(string ics, string name)
    {
        var line = GetContentLine(ics, name);
        return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    private static IEnumerable<string> GetEventContentLines(string ics)
    {
        var inEvent = false;
        foreach (var contentLine in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(contentLine, "BEGIN:VEVENT", StringComparison.Ordinal))
            {
                inEvent = true;
            }
            else if (string.Equals(contentLine, "END:VEVENT", StringComparison.Ordinal))
            {
                inEvent = false;
            }
            else if (inEvent)
            {
                yield return contentLine;
            }
        }
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

    private static TimeZoneInfo CreateTestTimeZone(string id = "Test/New_York")
    {
        // A synthetic time zone keeps the expected output independent of the platform's time zone database.
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-5), "Test Eastern", "EST", "EDT", [rule]);
    }

    [Fact]
    public void ToIcs_WritesTheStartAndEndWithTheTimeZoneIdentifier()
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified),
            TimeZone = CreateTestTimeZone(),
        };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("DTSTART;TZID=Test/New_York:20240102T080000", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND;TZID=Test/New_York:20240102T090000", GetContentLine(ics, "DTEND"));
    }

    [Fact]
    public void ToIcs_ConvertsAUtcStartToTheEventTimeZone()
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 02, 13, 00, 00, DateTimeKind.Utc),
            End = new DateTime(2024, 01, 02, 14, 00, 00, DateTimeKind.Utc),
            TimeZone = CreateTestTimeZone(),
        };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("DTSTART;TZID=Test/New_York:20240102T080000", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND;TZID=Test/New_York:20240102T090000", GetContentLine(ics, "DTEND"));
    }

    [Fact]
    public void ToIcs_ConvertsALocalStartToTheEventTimeZone()
    {
        var timeZone = CreateTestTimeZone();
        var start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Local);
        var @event = new Event
        {
            Start = start,
            End = start.AddHours(1),
            TimeZone = timeZone,
        };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        var expected = TimeZoneInfo.ConvertTime(start, TimeZoneInfo.Local, timeZone).ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
        Assert.Equal("DTSTART;TZID=Test/New_York:" + expected, GetContentLine(ics, "DTSTART"));
    }

    [Fact]
    public void ToIcs_WritesTheVTimeZoneComponent()
    {
        var @event = CreateEvent();
        @event.Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified);
        @event.End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified);
        @event.TimeZone = CreateTestTimeZone();

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // The components are anchored in the year of the event: 2024-03-10 is the second Sunday of March 2024
        var expected = string.Join("\r\n",
            "BEGIN:VTIMEZONE",
            "TZID:Test/New_York",
            "BEGIN:DAYLIGHT",
            "DTSTART:20240310T020000",
            "TZOFFSETFROM:-0500",
            "TZOFFSETTO:-0400",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "DTSTART:20241103T020000",
            "TZOFFSETFROM:-0400",
            "TZOFFSETTO:-0500",
            "RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU",
            "END:STANDARD",
            "END:VTIMEZONE") + "\r\n";

        Assert.Contains(expected, ics);
    }

    [Fact]
    public void ToIcs_WritesTheVTimeZoneComponentBeforeTheFirstEvent()
    {
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.True(ics.IndexOf("BEGIN:VTIMEZONE", StringComparison.Ordinal) < ics.IndexOf("BEGIN:VEVENT", StringComparison.Ordinal));
    }

    [Fact]
    public void ToIcs_WritesTheVTimeZoneComponentOnceForEventsSharingATimeZone()
    {
        var timeZone = CreateTestTimeZone();
        var calendar = new InternetCalendar();
        for (var i = 0; i < 2; i++)
        {
            var @event = CreateEvent();
            @event.TimeZone = timeZone;
            calendar.Events.Add(@event);
        }

        var ics = calendar.ToIcs();

        Assert.Equal(1, CountContentLines(ics, "BEGIN:VTIMEZONE"));
        Assert.Equal(2, CountContentLines(ics, "BEGIN:VEVENT"));
    }

    [Fact]
    public void ToIcs_WritesOneVTimeZoneComponentPerDistinctTimeZone()
    {
        var calendar = new InternetCalendar();
        foreach (var id in new[] { "Test/New_York", "Test/Chicago" })
        {
            var @event = CreateEvent();
            @event.TimeZone = CreateTestTimeZone(id);
            calendar.Events.Add(@event);
        }

        var ics = calendar.ToIcs();

        Assert.Equal(2, CountContentLines(ics, "BEGIN:VTIMEZONE"));
        Assert.Equal(1, CountContentLines(ics, "TZID:Test/New_York"));
        Assert.Equal(1, CountContentLines(ics, "TZID:Test/Chicago"));
    }

    [Fact]
    public void ToIcs_WritesASingleStandardComponentForATimeZoneWithoutDaylightSavingTime()
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Kolkata", TimeSpan.FromMinutes(330), "Test India", "IST");

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        var expected = string.Join("\r\n",
            "BEGIN:VTIMEZONE",
            "TZID:Test/Kolkata",
            "BEGIN:STANDARD",
            "DTSTART:19700101T000000",
            "TZOFFSETFROM:+0530",
            "TZOFFSETTO:+0530",
            "END:STANDARD",
            "END:VTIMEZONE") + "\r\n";

        Assert.Contains(expected, ics);
        Assert.DoesNotContain("BEGIN:DAYLIGHT", ics);
    }

    [Fact]
    public void ToIcs_PrefersTheOngoingPatternOverAYearSpecificRule()
    {
        // Unix materializes every year as its own fixed-date rule spanning only that year's daylight saving
        // window, and states the pattern itself in a single open-ended floating rule. Expanding a year-specific
        // rule yearly would claim the transition always falls on that same day of the month.
        var rules = new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2024, 3, 10),
                new DateTime(2024, 11, 2),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, day: 10),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, day: 3)),
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2024, 11, 4),
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday)),
        };

        var @event = CreateEvent();
        @event.Start = new DateTime(2024, 03, 06, 09, 00, 00, DateTimeKind.Unspecified);
        @event.End = new DateTime(2024, 03, 06, 10, 00, 00, DateTimeKind.Unspecified);
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Materialized", TimeSpan.FromHours(-5), "Test Eastern", "EST", "EDT", rules);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU\r\n", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU\r\n", ics);
        Assert.DoesNotContain("BYMONTHDAY", ics);
    }

    [Fact]
    public void ToIcs_AnchorsTheTransitionsInTheYearOfTheEvent()
    {
        var @event = CreateEvent();
        @event.Start = new DateTime(2024, 03, 06, 09, 00, 00, DateTimeKind.Unspecified);
        @event.End = new DateTime(2024, 03, 06, 10, 00, 00, DateTimeKind.Unspecified);
        @event.TimeZone = CreateTestTimeZone();

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // The rule starts in 2007, but anchoring the component there would leave a consumer applying the
        // transitions only to later years. 2024-03-10 is the second Sunday of March 2024.
        Assert.Contains("DTSTART:20240310T020000\r\n", ics);
        Assert.Contains("DTSTART:20241103T020000\r\n", ics);
    }

    [Fact]
    public void ToIcs_WritesASingleStandardComponentWhenTheTimeZoneNoLongerObservesDaylightSavingTime()
    {
        // A time zone that observed daylight saving time in the past still reports the expired rules.
        var expired = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(1942, 1, 1),
            new DateTime(1945, 10, 15),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), month: 1, day: 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), month: 10, day: 14));

        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Kolkata2", TimeSpan.FromMinutes(330), "Test India", "IST", "IDT", [expired]);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.DoesNotContain("BEGIN:DAYLIGHT", ics);
        Assert.Contains("TZOFFSETTO:+0530\r\n", ics);
        Assert.DoesNotContain("TZOFFSETTO:+0630", ics);
    }

    [Fact]
    public void ToIcs_WritesTheLastWeekOfTheMonthAsTheMinusOneOrdinal()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), month: 3, week: 5, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 10, week: 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd);
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Paris", TimeSpan.FromHours(1), "Test Paris", "CET", "CEST", [rule]);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU\r\n", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU\r\n", ics);
    }

    [Fact]
    public void ToIcs_WritesAFixedDateTransitionAsAMonthDay()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), month: 4, day: 1);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), month: 10, day: 15);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd);
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Fixed", TimeSpan.Zero, "Test Fixed", "STD", "DST", [rule]);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=1\r\n", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=10;BYMONTHDAY=15\r\n", ics);
    }

    [Fact]
    public void ToIcs_WritesAZeroOffsetWithAPlusSign()
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Utc", TimeSpan.Zero, "Test UTC", "UTC");

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Contains("TZOFFSETFROM:+0000\r\n", ics);
        Assert.Contains("TZOFFSETTO:+0000\r\n", ics);
        Assert.DoesNotContain("-0000", ics);
    }

    [Fact]
    public void ToIcs_WritesTheRecurrenceUntilInUtcWhenTheEventHasATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 01, 08, 00, 00, DateTimeKind.Unspecified);
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // 08:00 in a -05:00 time zone is 13:00Z
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T130000Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesARecurrenceUntilInAForwardGapAtTheOffsetBeforeIt()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 10, 02, 30, 00, DateTimeKind.Unspecified);
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // 02:30 does not exist on the transition day, so it is read at the -05:00 offset in effect before the gap
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240310T073000Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesAnAmbiguousRecurrenceUntilAtItsFirstOccurrence()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 11, 03, 01, 30, 00, DateTimeKind.Unspecified);
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // 01:30 happens twice and RFC 5545 keeps the first, at -04:00. TimeZoneInfo.ConvertTimeToUtc would have
        // resolved it to the second one and written 20241103T063000Z.
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20241103T053000Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesARecurrenceUntilUsingTheStandardOffsetOfItsOwnPeriod()
    {
        // A time zone whose standard offset itself shifts, which is what makes BaseUtcOffset the wrong thing to
        // subtract for a time inside the gap.
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2007, 1, 1),
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd,
            baseUtcOffsetDelta: TimeSpan.FromHours(1));
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Shifted", TimeSpan.FromHours(-5), "Test Shifted", "STD", "DST", [rule]);

        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 10, 02, 30, 00, DateTimeKind.Unspecified);
        var @event = CreateEvent();
        @event.TimeZone = timeZone;
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // The offset before the gap is -04:00, the base offset plus the rule's delta, so UNTIL is 06:30Z.
        // Subtracting BaseUtcOffset alone would have written 07:30Z.
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240310T063000Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_KeepsTheRecurrenceUntilUnchangedWithoutATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 01, 08, 00, 00, DateTimeKind.Unspecified);
        var @event = CreateEvent();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T080000", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_KeepsTheTimeStampsUnchangedWhenTheEventHasATimeZone()
    {
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        foreach (var name in new[] { "CREATED", "LAST-MODIFIED", "DTSTAMP" })
        {
            Assert.DoesNotContain(";TZID=", GetContentLine(ics, name));
            Assert.Matches(@"^\d{8}T\d{6}Z?$", GetContentLineValue(ics, name));
        }
    }

    [Theory]
    [InlineData("Evil\r\nBEGIN:VEVENT")]
    [InlineData("Evil;TZID=Other")]
    [InlineData("Evil:INJECTED")]
    [InlineData("Evil,Other")]
    [InlineData("Evil\"Quoted")]
    [InlineData("Evil\u0007Bell")]
    public void ToIcs_ThrowsWhenTheTimeZoneIdentifierIsNotSafe(string id)
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id);

        Assert.Throws<InvalidOperationException>(() => CreateCalendarWithEvent(@event).ToIcs());
    }

    [Fact]
    public void ToIcs_ThrowsBeforeWritingAnythingWhenTheTimeZoneIdentifierIsNotSafe()
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone("Evil\r\nBEGIN:VEVENT", TimeSpan.Zero, "Evil", "Evil");
        var calendar = CreateCalendarWithEvent(@event);

        using var writer = new StringWriter();
        Assert.Throws<InvalidOperationException>(() => calendar.ToIcs(writer));
        Assert.Equal("", writer.ToString());
    }

    [Theory]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Etc/GMT+5")]
    [InlineData("Romance Standard Time")]
    [InlineData("America/New_York")]
    public void ToIcs_AcceptsCommonTimeZoneIdentifiers(string id)
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Contains("TZID:" + id + "\r\n", ics);
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    [Fact]
    public void ToIcs_WritesAWellFormedVTimeZoneForASystemTimeZone()
    {
        // An IANA identifier does not resolve on Windows when globalization is invariant.
        var @event = CreateEvent();
        @event.Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified);
        @event.End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified);
        @event.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // The adjustment rules a platform reports vary, so only the shape of the component is asserted
        Assert.Equal(1, CountContentLines(ics, "BEGIN:VTIMEZONE"));
        Assert.Equal(1, CountContentLines(ics, "END:VTIMEZONE"));
        Assert.Contains("TZID:America/New_York\r\n", ics);
        Assert.Equal(1, CountContentLines(ics, "BEGIN:STANDARD"));

        foreach (var contentLine in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (contentLine.StartsWith("TZOFFSET", StringComparison.Ordinal))
            {
                Assert.Matches(@"^TZOFFSET(FROM|TO):[+-]\d{4}(\d{2})?$", contentLine);
            }
            else if (contentLine.StartsWith("RRULE:", StringComparison.Ordinal))
            {
                Assert.StartsWith("RRULE:FREQ=YEARLY;BYMONTH=", contentLine);
            }
            else if (contentLine.StartsWith("DTSTART:", StringComparison.Ordinal))
            {
                // A sub-component DTSTART is a local time: no Z suffix and no TZID parameter
                Assert.Matches(@"^DTSTART:\d{8}T\d{6}$", contentLine);
            }
        }
    }
#endif

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
            var value = GetContentLineValue(ics, name);
            Assert.Matches(@"^\d{8}T\d{6}Z?$", value);
        }
    }

    private static string CreateIcs(params string[] eventContentLines)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Test//Test//EN", "BEGIN:VEVENT" };
        lines.AddRange(eventContentLines);
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static Event ParseSingleEvent(params string[] eventContentLines)
    {
        var calendar = InternetCalendar.Parse(CreateIcs(eventContentLines));
        return Assert.Single(calendar.Events);
    }

    [Fact]
    public void Parse_ReadsTheCalendarProperties()
    {
        var calendar = InternetCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//Test//EN\r\nMETHOD:REQUEST\r\nEND:VCALENDAR\r\n");

        Assert.Equal("2.0", calendar.Version);
        Assert.Empty(calendar.Events);

        // PRODID identifies the writer, so it is not carried over to the parsed calendar.
        Assert.Equal(new[] { KeyValuePair.Create("METHOD", "REQUEST") }, calendar.AdditionalProperties);
    }

    [Fact]
    public void Parse_ReadsTheEventProperties()
    {
        var @event = ParseSingleEvent(
            "UID:event-1",
            "SUMMARY:Meeting",
            "DESCRIPTION:Agenda",
            "STATUS:CONFIRMED",
            "ORGANIZER:mailto:organizer@meziantou.net",
            "ATTENDEE:mailto:first@meziantou.net",
            "ATTENDEE:mailto:second@meziantou.net",
            "CREATED:20141208T100900Z",
            "LAST-MODIFIED:19960817T133000Z",
            "DTSTAMP:20141208T100900Z",
            "DTSTART:20240102T080000Z",
            "DTEND:20240102T090000Z",
            "X-MICROSOFT-CDO-BUSYSTATUS:OOF");

        Assert.Equal("event-1", @event.Id);
        Assert.Equal("Meeting", @event.Summary);
        Assert.Equal("Agenda", @event.Description);
        Assert.Equal(EventStatus.Confirmed, @event.Status);
        Assert.Equal("mailto:organizer@meziantou.net", @event.Organizer?.Address?.ToString());
        Assert.Equal(["mailto:first@meziantou.net", "mailto:second@meziantou.net"], @event.Attendees.Select(a => a.Address?.ToString()));
        Assert.Equal(new DateTime(2014, 12, 08, 10, 09, 00, DateTimeKind.Utc), @event.Created);
        Assert.Equal(new DateTime(1996, 08, 17, 13, 30, 00, DateTimeKind.Utc), @event.LastModified);
        Assert.Equal(new DateTime(2014, 12, 08, 10, 09, 00, DateTimeKind.Utc), @event.DateTimeStamp);
        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc), @event.Start);
        Assert.Equal(new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Utc), @event.End);
        Assert.Null(@event.TimeZone);
        Assert.Equal(new[] { KeyValuePair.Create("X-MICROSOFT-CDO-BUSYSTATUS", "OOF") }, @event.AdditionalProperties);
    }

    [Theory]
    [InlineData("TENTATIVE", EventStatus.Tentative)]
    [InlineData("CONFIRMED", EventStatus.Confirmed)]
    [InlineData("CANCELLED", EventStatus.Cancelled)]
    [InlineData("cancelled", EventStatus.Cancelled)]
    public void Parse_ReadsTheStatus(string value, EventStatus expected)
    {
        Assert.Equal(expected, ParseSingleEvent("STATUS:" + value).Status);
    }

    [Fact]
    public void Parse_RejectsAStatusThatCannotDescribeAnEvent()
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("STATUS:NEEDS-ACTION"), out _, out var error));
        Assert.Contains("NEEDS-ACTION", error);
    }

    [Fact]
    public void Parse_ReadsAFloatingDateTime()
    {
        var @event = ParseSingleEvent("DTSTART:20240102T080000");

        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified), @event.Start);
        Assert.Equal(DateTimeKind.Unspecified, @event.Start.Kind);
        Assert.Null(@event.TimeZone);
    }

    [Fact]
    public void Parse_ReadsADateValueAsItsFirstInstant()
    {
        var @event = ParseSingleEvent("DTSTART;VALUE=DATE:20240102");

        Assert.Equal(new DateTime(2024, 01, 02, 00, 00, 00, DateTimeKind.Unspecified), @event.Start);
    }

    [Fact]
    public void Parse_ReadsTheTimeZoneNamedByTheTzidParameter()
    {
        // UTC is the only identifier every platform is guaranteed to resolve.
        var @event = ParseSingleEvent("DTSTART;TZID=UTC:20240102T080000", "DTEND;TZID=UTC:20240102T090000");

        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified), @event.Start);
        Assert.Equal(new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified), @event.End);
    }

    [Fact]
    public void Parse_RejectsATimeZoneThatIsNotFound()
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("DTSTART;TZID=Nowhere/Unknown:20240102T080000"), out _, out var error));
        Assert.Contains("Nowhere/Unknown", error);
    }

    [Fact]
    public void Parse_RejectsAnEventWhosePropertiesUseTwoTimeZones()
    {
        var ics = CreateIcs("DTSTART;TZID=UTC:20240102T080000", "DTEND;TZID=Europe/Paris:20240102T090000");

        Assert.False(InternetCalendar.TryParse(ics, out _, out var error));
        Assert.Contains("two different time zones", error);
    }

    [Fact]
    public void Parse_RejectsAUtcDateTimeCarryingATzidParameter()
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("DTSTART;TZID=UTC:20240102T080000Z"), out _, out var error));
        Assert.Contains("TZID", error);
    }

    [Fact]
    public void Parse_ReadsTheRecurrenceRule()
    {
        var @event = ParseSingleEvent("RRULE:FREQ=WEEKLY;INTERVAL=3;BYDAY=TU");

        Assert.Equal("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU", @event.RecurrenceRule?.Text);
    }

    [Fact]
    public void Parse_RejectsAnInvalidRecurrenceRule()
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("RRULE:FREQ=NEVER"), out _, out var error));
        Assert.Contains("FREQ=NEVER", error);
    }

    [Fact]
    public void Parse_UnfoldsAContentLine()
    {
        // RFC 5545 section 3.1: a CRLF followed by a single white space continues the previous content line.
        var @event = ParseSingleEvent("SUMMARY:A very\r\n  long\r\n\t summary");

        Assert.Equal("A very long summary", @event.Summary);
    }

    [Fact]
    public void Parse_UnescapesATextValue()
    {
        var @event = ParseSingleEvent(@"SUMMARY:a\, b\; c\\ d\ne");

        Assert.Equal("a, b; c\\ d\ne", @event.Summary);
    }

    [Fact]
    public void Parse_KeepsAColonInsideAQuotedParameterValue()
    {
        // The colon inside the quoted value must not be taken for the start of the property value.
        var @event = ParseSingleEvent("DTSTART;X-NOTE=\"a:b\";TZID=UTC:20240102T080000");

        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified), @event.Start);
    }

    [Fact]
    public void Parse_ReadsSeveralEvents()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\nUID:first\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:second\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        var calendar = InternetCalendar.Parse(ics);

        Assert.Equal(["first", "second"], calendar.Events.Select(e => e.Id));
    }

    [Fact]
    public void Parse_SkipsTheComponentsTheModelDoesNotRepresent()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "BEGIN:VTIMEZONE\r\nTZID:UTC\r\nBEGIN:STANDARD\r\nDTSTART:19700101T000000\r\nTZOFFSETFROM:+0000\r\nTZOFFSETTO:+0000\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n" +
            "BEGIN:VTODO\r\nUID:todo\r\nEND:VTODO\r\n" +
            "BEGIN:VEVENT\r\nUID:event\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nEND:VALARM\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        var calendar = InternetCalendar.Parse(ics);

        var @event = Assert.Single(calendar.Events);
        Assert.Equal("event", @event.Id);
        Assert.Empty(@event.AdditionalProperties);
    }

    [Theory]
    [InlineData("")]
    [InlineData("VERSION:2.0\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n")]
    [InlineData(" continuation\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nVERSION\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\n;PARAM=1:value\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nNAME;=1:value\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nNAME;PARAM:value\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nNAME;PARAM=\"unterminated:value\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:not-a-date\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    public void TryParse_ReturnsFalseForInvalidContent(string ics)
    {
        Assert.False(InternetCalendar.TryParse(ics, out var calendar, out var error));
        Assert.Null(calendar);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_ThrowsForInvalidContent()
    {
        Assert.Throws<FormatException>(() => InternetCalendar.Parse("BEGIN:VTODO\r\nEND:VTODO\r\n"));
    }

    [Fact]
    public void Parse_ReadsAStream()
    {
        using var stream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(CreateIcs("SUMMARY:Réunion")));

        var calendar = InternetCalendar.Parse(stream);

        Assert.Equal("Réunion", Assert.Single(calendar.Events).Summary);
    }

    [Fact]
    public void Parse_ReadsTheOutputOfToIcs()
    {
        var calendar = new InternetCalendar();
        calendar.AdditionalProperties["METHOD"] = "REQUEST";
        calendar.Events.Add(new Event
        {
            Id = "event-1",
            Summary = "A summary; with, escapes\\",
            Description = "Line 1\nLine 2",
            Status = EventStatus.Cancelled,
            Organizer = new Organizer { Address = new InternetCalendarUserAddress("organizer@meziantou.net") },
            Attendees = { new Attendee { Address = new InternetCalendarUserAddress("attendee@meziantou.net") } },
            Created = new DateTime(2014, 12, 08, 10, 09, 00, DateTimeKind.Utc),
            LastModified = new DateTime(2014, 12, 08, 10, 09, 00, DateTimeKind.Utc),
            DateTimeStamp = new DateTime(2014, 12, 08, 10, 09, 00, DateTimeKind.Utc),
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Utc),
            RecurrenceRule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=10"),
            AdditionalProperties = { ["X-MICROSOFT-CDO-BUSYSTATUS"] = "OOF" },
        });

        var parsed = InternetCalendar.Parse(calendar.ToIcs());

        Assert.Equal(calendar.ToIcs(), parsed.ToIcs());

        var expected = Assert.Single(calendar.Events);
        var actual = Assert.Single(parsed.Events);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Organizer?.Address?.Uri, actual.Organizer?.Address?.Uri);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.End, actual.End);
        Assert.Equal(expected.RecurrenceRule?.Text, actual.RecurrenceRule?.Text);
    }

    [Fact]
    public void Parse_ReadsTheOutputOfToIcsForAnEventWithATimeZone()
    {
        var recurrenceRule = RecurrenceRule.Parse("FREQ=DAILY");
        recurrenceRule.EndDate = new DateTime(2024, 02, 01, 08, 00, 00, DateTimeKind.Unspecified);

        var calendar = CreateCalendarWithEvent(new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified),
            TimeZone = TimeZoneInfo.Utc,
            RecurrenceRule = recurrenceRule,
        });

        var parsed = InternetCalendar.Parse(calendar.ToIcs());

        var @event = Assert.Single(parsed.Events);
        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified), @event.Start);
        Assert.Equal(new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified), @event.End);

        // The writer turned the floating UNTIL into a UTC one, which the parser reads back as an instant.
        Assert.Equal(new DateTime(2024, 02, 01, 08, 00, 00, DateTimeKind.Utc), @event.RecurrenceRule?.EndDate);
        Assert.Equal(calendar.ToIcs(), parsed.ToIcs());
    }

    [Fact]
    public void Parse_ReadsASlicedSpan()
    {
        var ics = CreateIcs("SUMMARY:Meeting");
        var padded = "﻿leading" + ics + "trailing";

        var calendar = InternetCalendar.Parse(padded.AsSpan("﻿leading".Length, ics.Length));

        Assert.Equal("Meeting", Assert.Single(calendar.Events).Summary);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Parse_AcceptsEveryLineBreak(string lineBreak)
    {
        var ics = string.Join(lineBreak, "BEGIN:VCALENDAR", "BEGIN:VEVENT", "SUMMARY:A", "  summary", "END:VEVENT", "END:VCALENDAR") + lineBreak;

        Assert.Equal("A summary", Assert.Single(InternetCalendar.Parse(ics.AsSpan()).Events).Summary);
    }

    [Fact]
    public void Parse_ReadsTheSameCalendarFromASpanAndFromATextReader()
    {
        var ics = CreateIcs("UID:event-1", "SUMMARY:Meeting", "DTSTART:20240102T080000Z");

        using var reader = new StringReader(ics);
        Assert.Equal(InternetCalendar.Parse(ics.AsSpan()).ToIcs(), InternetCalendar.Parse(reader).ToIcs());
    }

    [Fact]
    public void TryParse_ReturnsFalseForANullString()
    {
        Assert.False(InternetCalendar.TryParse(ics: null, out var calendar, out var error));
        Assert.Null(calendar);
        Assert.NotNull(error);
    }
}
