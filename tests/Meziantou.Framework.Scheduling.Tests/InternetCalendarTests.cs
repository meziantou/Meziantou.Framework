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

    private static Event CreateFloatingEvent()
    {
        return new Event
        {
            Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified),
            End = new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified),
        };
    }

    [Fact]
    public void ToIcs_KeepsTheRecurrenceUntilUnchangedWithoutATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 01, 08, 00, 00, DateTimeKind.Unspecified);
        var @event = CreateFloatingEvent();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T080000", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_FloatingRecurrenceUntil_RoundTrips()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 03, 01, 08, 00, 00, DateTimeKind.Unspecified);
        var @event = CreateFloatingEvent();
        @event.RecurrenceRule = rrule;

        var parsed = InternetCalendar.Parse(CreateCalendarWithEvent(@event).ToIcs());

        var recurrenceRule = parsed.Events.Single().RecurrenceRule;
        Assert.Equal("FREQ=DAILY;UNTIL=20240301T080000", recurrenceRule?.Text);
        Assert.Equal(DateTimeKind.Unspecified, recurrenceRule?.EndDate?.Kind);
    }

    [Fact]
    public void ToIcs_DateRecurrenceUntil_OfAnAllDayEvent_IsWrittenBackAsADate()
    {
        var calendar = InternetCalendar.Parse(CreateIcs("DTSTART;VALUE=DATE:20240101", "RRULE:FREQ=DAILY;UNTIL=20240105"));

        var ics = calendar.ToIcs();

        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240105", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_DateRecurrenceUntil_IsWrittenInUtcWhenTheEventHasATimeZone()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240301");
        var @event = CreateEvent();
        @event.TimeZone = CreateTestTimeZone();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // A DATE UNTIL includes the whole day, whose last second in a -05:00 time zone is 04:59:59Z the next day
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240302T045959Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesAUtcUntilAsFloatingWhenTheTimeZoneOfTheStartIsUnresolved()
    {
        var calendar = InternetCalendar.Parse(CreateIcs("DTSTART;TZID=Custom/Unknown:20240101T100000", "RRULE:FREQ=DAILY;UNTIL=20240105T090000Z"));

        var ics = calendar.ToIcs();

        Assert.Equal("DTSTART:20240101T100000", GetContentLine(ics, "DTSTART"));
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240105T090000", GetContentLine(ics, "RRULE"));
        Assert.Equal(ics, InternetCalendar.Parse(ics).ToIcs());
    }

    [Fact]
    public void ToIcs_WritesAFloatingUntilInUtcWhenTheStartIsUtc()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240301T080000");
        var @event = CreateEvent();
        @event.RecurrenceRule = rrule;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("DTSTART:20240102T080000Z", GetContentLine(ics, "DTSTART"));
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T080000Z", GetContentLine(ics, "RRULE"));

        // The rule of the event is not modified.
        Assert.Equal("FREQ=DAILY;UNTIL=20240301T080000", rrule.Text);
        Assert.Equal(DateTimeKind.Unspecified, rrule.EndDate?.Kind);
    }

    [Fact]
    public void ToIcs_WritesAFloatingUntilInUtcWhenTheStartIsLocal()
    {
        var until = new DateTime(2024, 03, 01, 08, 00, 00, DateTimeKind.Unspecified);
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = until;
        var @event = new Event { Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Local), RecurrenceRule = rrule };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // A floating UNTIL bounds the local occurrences by their wall clock, so it is read in the local time zone
        var expected = TimeZoneInfo.ConvertTimeToUtc(until, TimeZoneInfo.Local).ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture) + "Z";
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=" + expected, GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesADateUntilInUtcWhenTheStartIsUtc()
    {
        var @event = CreateEvent();
        @event.RecurrenceRule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240301");

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T235959Z", GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesADateUntilAsTheEndOfTheDayWhenTheStartIsFloating()
    {
        var @event = CreateFloatingEvent();
        @event.RecurrenceRule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240301");

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=20240301T235959", GetContentLine(ics, "RRULE"));
    }

    [Theory]
    [InlineData("FREQ=DAILY;UNTIL=20240105T090000", "20240105")]
    [InlineData("FREQ=DAILY;UNTIL=20240105T000000", "20240105")]
    [InlineData("FREQ=DAILY;UNTIL=20240105T233000Z", "20240105")]
    public void ToIcs_WritesTheUntilOfAnAllDayEventAsADate(string rrule, string expected)
    {
        var @event = new Event { Start = new DateTime(2024, 01, 01), IsAllDay = true, RecurrenceRule = RecurrenceRule.Parse(rrule) };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("DTSTART;VALUE=DATE:20240101", GetContentLine(ics, "DTSTART"));
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=" + expected, GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_WritesTheUntilOfAnAllDayEventAsADateInTheFrameOfTheStart()
    {
        var rrule = RecurrenceRule.Parse("FREQ=DAILY");
        rrule.EndDate = new DateTime(2024, 01, 05, 23, 30, 00, DateTimeKind.Local);
        var @event = new Event { Start = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc), IsAllDay = true, RecurrenceRule = rrule };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // The start is a UTC value, so the date of the local UNTIL is taken in UTC
        var expected = rrule.EndDate.Value.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        Assert.Equal("RRULE:FREQ=DAILY;UNTIL=" + expected, GetContentLine(ics, "RRULE"));
    }

    [Fact]
    public void ToIcs_KeepsTheTimeStampsUnchangedWhenTheEventHasATimeZone()
    {
        var @event = CreateEvent();
        @event.Created = new DateTime(2023, 12, 01, 10, 00, 00, DateTimeKind.Utc);
        @event.LastModified = new DateTime(2023, 12, 02, 10, 00, 00, DateTimeKind.Utc);
        @event.DateTimeStamp = new DateTime(2023, 12, 03, 10, 00, 00, DateTimeKind.Utc);
        @event.TimeZone = CreateTestTimeZone();

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("CREATED:20231201T100000Z", GetContentLine(ics, "CREATED"));
        Assert.Equal("LAST-MODIFIED:20231202T100000Z", GetContentLine(ics, "LAST-MODIFIED"));
        Assert.Equal("DTSTAMP:20231203T100000Z", GetContentLine(ics, "DTSTAMP"));
    }

    [Fact]
    public void ToIcs_OmitsTheDateTimesThatAreNotSet()
    {
        var @event = new Event { Start = new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc) };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("DTSTART:20240102T080000Z", GetContentLine(ics, "DTSTART"));
        foreach (var name in new[] { "CREATED", "LAST-MODIFIED", "DTSTAMP", "DTEND" })
        {
            Assert.DoesNotContain(GetEventContentLines(ics), line => line.StartsWith(name + ":", StringComparison.Ordinal), name);
        }

        Assert.DoesNotContain("00010101", ics);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToIcs_WritesTheTimeStampsInUtc(DateTimeKind kind)
    {
        var @event = CreateEvent();
        @event.Created = new DateTime(2023, 12, 01, 10, 00, 00, kind);
        @event.LastModified = new DateTime(2023, 12, 02, 10, 00, 00, kind);
        @event.DateTimeStamp = new DateTime(2023, 12, 03, 10, 00, 00, kind);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // RFC 5545 requires these properties in UTC, so an unspecified value is taken as UTC rather than written floating
        Assert.Equal("CREATED:20231201T100000Z", GetContentLine(ics, "CREATED"));
        Assert.Equal("LAST-MODIFIED:20231202T100000Z", GetContentLine(ics, "LAST-MODIFIED"));
        Assert.Equal("DTSTAMP:20231203T100000Z", GetContentLine(ics, "DTSTAMP"));
    }

    [Fact]
    public void ToIcs_ConvertsALocalTimeStampToUtc()
    {
        var created = new DateTime(2023, 12, 01, 10, 00, 00, DateTimeKind.Local);
        var @event = CreateEvent();
        @event.Created = created;

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("CREATED:" + created.ToUniversalTime().ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture) + "Z", GetContentLine(ics, "CREATED"));
    }

    [Theory]
    [InlineData("Evil\r\nBEGIN:VEVENT")]
    [InlineData("Evil\nBEGIN:VEVENT")]
    [InlineData("Evil\"Quoted")]
    [InlineData("Evil\";X-INJECTED=\"")]
    [InlineData("Evil\u0007Bell")]
    public void ToIcs_ThrowsWhenTheTimeZoneIdentifierIsNotSafe(string id)
    {
        var @event = CreateEvent();
        @event.TimeZone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id);

        Assert.Throws<InvalidOperationException>(() => CreateCalendarWithEvent(@event).ToIcs());
    }

    [Theory]
    [InlineData("Evil;TZID=Other", "\"Evil;TZID=Other\"", @"Evil\;TZID=Other")]
    [InlineData("Evil:INJECTED", "\"Evil:INJECTED\"", "Evil:INJECTED")]
    [InlineData("Evil,Other", "\"Evil,Other\"", @"Evil\,Other")]
    [InlineData("(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna", "\"(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna\"", @"(UTC+01:00) Amsterdam\, Berlin\, Bern\, Rome\, Stockholm\, Vienna")]
    [InlineData(@"Back\slash", @"Back\slash", @"Back\\slash")]
    [InlineData("Heure d'été", "Heure d'été", "Heure d'été")]
    public void ToIcs_QuotesATimeZoneIdentifierThatIsNotAParameterText(string id, string parameterValue, string propertyValue)
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 07, 01, 09, 00, 00),
            End = new DateTime(2024, 07, 01, 10, 00, 00),
            TimeZone = CreateTestTimeZone(id),
        };

        var ics = CreateCalendarWithEvent(@event).ToIcs();
        var unfolded = ics.Replace("\r\n ", "", StringComparison.Ordinal);

        // RFC 5545 section 3.1: a parameter value containing a colon, a semicolon or a comma is a quoted-string, while the
        // TZID property of the VTIMEZONE is a TEXT value, whose separators are escaped.
        Assert.Equal(1, CountContentLines(unfolded, "DTSTART;TZID=" + parameterValue + ":20240701T090000"));
        Assert.Equal(1, CountContentLines(unfolded, "TZID:" + propertyValue));

        var parsed = InternetCalendar.Parse(ics);
        var parsedEvent = Assert.Single(parsed.Events);
        Assert.NotNull(parsedEvent.TimeZone);
        Assert.Equal(id, parsedEvent.TimeZone.Id);
        Assert.Equal(new DateTime(2024, 07, 01, 13, 00, 00, DateTimeKind.Utc), TimeZoneInfo.ConvertTimeToUtc(parsedEvent.Start, parsedEvent.TimeZone));
        Assert.Equal(ics, parsed.ToIcs());
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

    private static void AssertLinesAreFolded(string ics)
    {
        foreach (var physicalLine in ics.Split("\r\n"))
        {
            // RFC 5545 section 3.1: 75 octets excluding the line break, and a fold never splits a character.
            Assert.True(Encoding.UTF8.GetByteCount(physicalLine) <= 75, physicalLine);
            if (physicalLine.Length > 0)
            {
                Assert.False(char.IsLowSurrogate(physicalLine[0]), physicalLine);
                Assert.False(char.IsHighSurrogate(physicalLine[^1]), physicalLine);
            }
        }
    }

    [Fact]
    public void ToIcs_FoldsALongContentLine()
    {
        var @event = CreateEvent();
        @event.Summary = string.Concat(Enumerable.Range(0, 15).Select(i => "Summary-" + i.ToString("00", CultureInfo.InvariantCulture)));

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.HasCount(150, @event.Summary);
        AssertLinesAreFolded(ics);
        Assert.Contains("\r\n ", ics);
        Assert.Equal(@event.Summary, Assert.Single(InternetCalendar.Parse(ics).Events).Summary);
    }

    [Fact]
    public void ToIcs_FoldsOnUtf8OctetsWithoutSplittingACharacter()
    {
        var emoji = char.ConvertFromUtf32(0x1F4C5);
        var @event = CreateEvent();
        @event.Summary = string.Concat(Enumerable.Repeat("é", 50)) + string.Concat(Enumerable.Repeat(emoji, 40)) + string.Concat(Enumerable.Repeat("日本", 30));

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        AssertLinesAreFolded(ics);
        using var stream = new MemoryStream();
        CreateCalendarWithEvent(@event).ToIcs(stream);
        stream.Position = 0;
        Assert.Equal(@event.Summary, Assert.Single(InternetCalendar.Parse(stream).Events).Summary);
    }

    [Fact]
    public void ToIcs_DoesNotFoldAContentLineOf75Octets()
    {
        var @event = CreateEvent();
        @event.Summary = new string('a', 75 - "SUMMARY:".Length);

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("SUMMARY:" + @event.Summary, GetContentLine(ics, "SUMMARY"));
    }

    [Fact]
    public void ToIcs_SkipsAnAttendeeWithoutAnAddress()
    {
        var @event = CreateEvent();
        @event.Attendees.Add(new Attendee());
        @event.Attendees.Add(new Attendee { Address = new InternetCalendarUserAddress("attendee@meziantou.net") });

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal(["ATTENDEE:mailto:attendee@meziantou.net"], GetEventContentLines(ics).Where(line => line.StartsWith("ATTENDEE", StringComparison.Ordinal)));
        Assert.Single(InternetCalendar.Parse(ics).Events[0].Attendees);
    }

    [Fact]
    public void ToIcs_ACalendarUserAddressDoesNotInjectAComponent()
    {
        var address = new InternetCalendarUserAddress("x\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:evil\r\nSUMMARY:Injected\r\nX-A:b@example.com");
        var @event = CreateEvent();
        @event.Organizer = new Organizer { Address = address };
        @event.Attendees.Add(new Attendee { Address = address });

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal(1, CountContentLines(ics, "BEGIN:VEVENT"));
        Assert.Equal("ORGANIZER:mailto:x%0D%0AEND:VEVENT%0D%0ABEGIN:VEVENT%0D%0AUID:evil%0D%0ASUMMARY:Injected%0D%0AX-A:b@example.com", GetContentLine(ics.Replace("\r\n ", "", StringComparison.Ordinal), "ORGANIZER"));
        var parsed = Assert.Single(InternetCalendar.Parse(ics).Events);
        Assert.Null(parsed.Summary);
        Assert.Equal(address.Uri, parsed.Organizer?.Address?.Uri);
        Assert.Equal(address.Uri, Assert.Single(parsed.Attendees).Address?.Uri);
    }

    [Fact]
    public void ToIcs_EscapesAControlCharacterOfACalendarUserAddress()
    {
        var @event = CreateEvent();
        @event.Attendees.Add(new Attendee { Address = new InternetCalendarUserAddress(new Uri("mailto:a" + (char)0x01 + "b@example.com")) });

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("ATTENDEE:mailto:a%01b@example.com", GetContentLine(ics, "ATTENDEE"));
        Assert.DoesNotContain((char)0x01, ics);
    }

    [Theory]
    [InlineData("ATTENDEE:mailto:john@example.com")]
    [InlineData("ATTENDEE:mailto:a%2520b@example.com")]
    [InlineData("ATTENDEE:mailto:a%20b@example.com")]
    [InlineData("ATTENDEE:urn:uuid:f81d4fae-7dec-11d0-a765-00a0c91e6bf6")]
    [InlineData("ATTENDEE:https://example.com/users/john?a=1")]
    public void Parse_WritesACalendarUserAddressBackUnchanged(string contentLine)
    {
        var calendar = InternetCalendar.Parse(CreateIcs("DTSTART:20240102T080000Z", contentLine));

        var ics = calendar.ToIcs();

        Assert.Equal(contentLine, GetContentLine(ics, "ATTENDEE"));
        Assert.Equal(ics, InternetCalendar.Parse(ics).ToIcs());
    }

    [Fact]
    public void ToIcs_WritesAnAbsoluteFileUriWithItsScheme()
    {
        var @event = CreateEvent();
        @event.Attendees.Add(new Attendee { Address = new InternetCalendarUserAddress(new Uri("file:///home/john")) });

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("ATTENDEE:file:///home/john", GetContentLine(ics, "ATTENDEE"));
        Assert.NotNull(Assert.Single(Assert.Single(InternetCalendar.Parse(ics).Events).Attendees).Address);
    }

    [Fact]
    public void InternetCalendarUserAddress_RejectsARelativeUri()
    {
        Assert.Throws<ArgumentException>(() => new InternetCalendarUserAddress(new Uri("foo", UriKind.Relative)));
    }

    [Fact]
    public void Parse_ReadsTheParametersOfTheOrganizerAndTheAttendees()
    {
        var @event = ParseSingleEvent(
            "DTSTART:20240102T080000Z",
            "ORGANIZER;CN=John Doe:mailto:john@example.com",
            "ATTENDEE;CN=Jane;PARTSTAT=ACCEPTED;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:jane@example.com",
            "ATTENDEE;CN=\"Doe, Joe\";DELEGATED-FROM=\"mailto:a@example.com\",\"mailto:b@example.com\":mailto:joe@example.com",
            "ATTENDEE:mailto:bob@example.com");

        Assert.Equal([new("CN", "John Doe")], @event.Organizer?.Parameters);
        Assert.Equal([new("CN", "Jane"), new("PARTSTAT", "ACCEPTED"), new("ROLE", "REQ-PARTICIPANT"), new("RSVP", "TRUE")], @event.Attendees[0].Parameters);
        Assert.Equal([new("CN", "\"Doe, Joe\""), new("DELEGATED-FROM", "\"mailto:a@example.com\",\"mailto:b@example.com\"")], @event.Attendees[1].Parameters);
        Assert.Empty(@event.Attendees[2].Parameters);
    }

    [Fact]
    public void Parse_WritesTheParametersOfTheOrganizerAndTheAttendeesBack()
    {
        string[] lines =
        [
            "ORGANIZER;CN=John Doe:mailto:john@example.com",
            "ATTENDEE;CN=Jane;PARTSTAT=ACCEPTED;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:jane@example.com",
            "ATTENDEE;CN=\"Doe, Joe\";DELEGATED-FROM=\"mailto:a@example.com\":mailto:joe@example.com",
        ];
        var calendar = InternetCalendar.Parse(CreateIcs(["DTSTART:20240102T080000Z", .. lines]));

        var ics = calendar.ToIcs().Replace("\r\n ", "", StringComparison.Ordinal);

        foreach (var line in lines)
        {
            Assert.Equal(1, CountContentLines(ics, line));
        }
    }

    [Fact]
    public void ToIcs_WritesTheParametersOfAnAttendeeInOrder()
    {
        var @event = CreateEvent();
        @event.Attendees.Add(new Attendee
        {
            Address = new InternetCalendarUserAddress("jane@example.com"),
            Parameters = { new("RSVP", "TRUE"), new("CN", "\"Doe, Jane\"") },
        });

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("ATTENDEE;RSVP=TRUE;CN=\"Doe, Jane\":mailto:jane@example.com", GetContentLine(ics, "ATTENDEE"));
    }

    [Theory]
    [InlineData("P\r\nX", "a")]
    [InlineData("", "a")]
    [InlineData("P", "a;X-EVIL=b")]
    [InlineData("P", "a:b")]
    [InlineData("P", "\"unterminated")]
    [InlineData("P", "a\"b")]
    [InlineData("P", "\"a\r\nb\"")]
    [InlineData("P", null)]
    public void Attendee_RejectsAParameterThatCannotBeWritten(string name, string? value)
    {
        var attendee = new Attendee();
        var organizer = new Organizer { Parameters = { new("CN", "a") } };

        Assert.Throws<ArgumentException>(() => attendee.Parameters.Add(new(name, value!)));
        Assert.Throws<ArgumentException>(() => organizer.Parameters.Insert(0, new(name, value!)));
        Assert.Throws<ArgumentException>(() => organizer.Parameters[0] = new(name, value!));
        Assert.Empty(attendee.Parameters);
        Assert.Equal([new("CN", "a")], organizer.Parameters);
    }

    [Fact]
    public void ToIcs_SkipsANullEvent()
    {
        var calendar = new InternetCalendar();
        calendar.Events.Add(null!);
        calendar.Events.Add(CreateEvent());

        var ics = calendar.ToIcs();

        Assert.Equal(1, CountContentLines(ics, "BEGIN:VEVENT"));
    }

    [Fact]
    public void ToIcs_DropsTheControlCharactersOfATextValue()
    {
        var value = "a" + (char)0x01 + "b\tc" + (char)0x7F + "d" + (char)0x1B + "\r\ne";
        var @event = CreateEvent();
        @event.Summary = value;
        @event.AdditionalProperties["X-A"] = value;
        var calendar = CreateCalendarWithEvent(@event);
        calendar.AdditionalProperties["X-B"] = value;

        var ics = calendar.ToIcs();

        Assert.Equal("SUMMARY:ab\tcd\\ne", GetContentLine(ics, "SUMMARY"));
        Assert.Equal("X-A:ab\tcd\\ne", GetContentLine(ics, "X-A"));
        Assert.Equal(1, CountContentLines(ics, "X-B:ab\tcd\\ne"));
        Assert.DoesNotContain(ics, c => c < 0x20 && c is not '\t' and not '\r' and not '\n');
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("2.0;2.0")]
    [InlineData("1.0;2.0")]
    public void Parse_WritesTheVersionBackUnchanged(string version)
    {
        var calendar = InternetCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:" + version + "\r\nPRODID:-//Test//Test//EN\r\nEND:VCALENDAR\r\n");

        Assert.Equal(version, calendar.Version);
        Assert.Equal(1, CountContentLines(calendar.ToIcs(), "VERSION:" + version));
    }

    [Fact]
    public void ToIcs_ThrowsBeforeWritingAnythingWhenTheVersionContainsALineBreak()
    {
        var calendar = new InternetCalendar { Version = "2.0\r\nBEGIN:VEVENT" };
        using var writer = new StringWriter();

        Assert.Throws<InvalidOperationException>(() => calendar.ToIcs(writer));
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void ToIcs_OmitsTheDescriptionWhenItIsNotSet()
    {
        var ics = CreateCalendarWithEvent(CreateEvent()).ToIcs();

        Assert.DoesNotContain(GetEventContentLines(ics), line => line.StartsWith("DESCRIPTION", StringComparison.Ordinal));
        Assert.Null(Assert.Single(InternetCalendar.Parse(ics).Events).Description);
    }

    [Fact]
    public void ToIcs_OmitsTheStartWhenItIsNotSet()
    {
        var ics = CreateCalendarWithEvent(new Event { Summary = "No start" }).ToIcs();

        Assert.DoesNotContain(GetEventContentLines(ics), line => line.StartsWith("DTSTART", StringComparison.Ordinal));
        Assert.DoesNotContain("00010101", ics);
        Assert.Equal(default, Assert.Single(InternetCalendar.Parse(ics).Events).Start);
    }

    [Fact]
    public void ToIcs_OmitsTheStatusWhenItIsNotSet()
    {
        var ics = CreateCalendarWithEvent(CreateEvent()).ToIcs();

        Assert.DoesNotContain(GetEventContentLines(ics), line => line.StartsWith("STATUS", StringComparison.Ordinal));
        Assert.Null(Assert.Single(InternetCalendar.Parse(ics).Events).Status);
    }

    [Fact]
    public void ToIcs_WritesAnAllDayEventWithDateValues()
    {
        var @event = new Event
        {
            Start = new DateTime(2024, 01, 01, 13, 00, 00, DateTimeKind.Utc),
            End = new DateTime(2024, 01, 02),
            IsAllDay = true,
            TimeZone = CreateTestTimeZone(),
        };

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        // A DATE value denotes a day wherever the reader is, so neither the time nor the time zone is written
        Assert.Equal("DTSTART;VALUE=DATE:20240101", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND;VALUE=DATE:20240102", GetContentLine(ics, "DTEND"));
        Assert.DoesNotContain("VTIMEZONE", ics);
        Assert.DoesNotContain("TZID", ics);
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

    [Theory]
    [InlineData("BEGIN", "VEVENT", 1)]
    [InlineData("END", "VCALENDAR", 1)]
    [InlineData("VERSION", "3.0", 0)]
    [InlineData("prodid", "-//Evil//EN", 0)]
    public void ToIcs_SkipsACalendarAdditionalPropertyNamedAsAPropertyTheCalendarIsWrittenWith(string name, string value, int expectedCount)
    {
        var calendar = CreateCalendarWithEvent(CreateEvent());
        calendar.AdditionalProperties[name] = value;
        calendar.RawProperties.Add(new InternetCalendarProperty(name, value));

        var ics = calendar.ToIcs();

        Assert.Equal(expectedCount, CountContentLines(ics, name + ":" + value));
        Assert.True(InternetCalendar.TryParse(ics, out _, out var error), error);
    }

    [Theory]
    [InlineData("END", "VEVENT", 1)]
    [InlineData("BEGIN", "VALARM", 0)]
    [InlineData("DTSTART", "20200101T000000Z", 0)]
    [InlineData("uid", "other-uid", 0)]
    [InlineData("SUMMARY", "Other summary", 0)]
    [InlineData("DESCRIPTION", "Other description", 0)]
    public void ToIcs_SkipsAnEventAdditionalPropertyNamedAsAPropertyTheEventIsWrittenWith(string name, string value, int expectedCount)
    {
        var @event = CreateEvent();
        @event.Id = "uid";
        @event.Summary = "Summary";
        @event.AdditionalProperties[name] = value;
        @event.RawProperties.Add(new InternetCalendarProperty(name, value));

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal(expectedCount, CountContentLines(ics, name + ":" + value));
        var parsed = Assert.Single(InternetCalendar.Parse(ics).Events);
        Assert.Equal("uid", parsed.Id);
        Assert.Equal(@event.Start, parsed.Start);
    }

    [Fact]
    public void ToIcs_WritesARawPropertyVerbatim()
    {
        var @event = CreateEvent();
        @event.RawProperties.Add(new InternetCalendarProperty("GEO", "37.386013;-122.082932"));
        @event.RawProperties.Add(new InternetCalendarProperty("EXDATE", [new("TZID", "Europe/Paris")], "20240104T100000,20240105T100000"));
        @event.RawProperties.Add(new InternetCalendarProperty("X-ALT", [new("ALTREP", "\"cid:part1@example.org\""), new("MEMBER", "\"mailto:a@example.org\",\"mailto:b@example.org\"")], "a:b"));

        var ics = CreateCalendarWithEvent(@event).ToIcs();

        Assert.Equal("GEO:37.386013;-122.082932", GetContentLine(ics, "GEO"));
        Assert.Equal("EXDATE;TZID=Europe/Paris:20240104T100000,20240105T100000", GetContentLine(ics, "EXDATE"));
        Assert.Contains("X-ALT;ALTREP=\"cid:part1@example.org\";MEMBER=\"mailto:a@example.org\",\"mailto:b@example.org\":a:b", ics.Replace("\r\n ", "", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X A", "value")]
    [InlineData("X-A\r\nEND:VCALENDAR", "value")]
    [InlineData("", "value")]
    [InlineData("X-A", "v\r\nEND:VCALENDAR")]
    [InlineData("X-A", "v\nEND:VCALENDAR")]
    public void InternetCalendarProperty_RejectsANameOrAValueThatCannotBeWritten(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => new InternetCalendarProperty(name, value));
    }

    [Theory]
    [InlineData("P\r\nX", "a")]
    [InlineData("P", "a;X-EVIL=b")]
    [InlineData("P", "a:b")]
    [InlineData("P", "\"unterminated")]
    [InlineData("P", "\"a\"b")]
    [InlineData("P", "a\"b")]
    [InlineData("P", "\"a\r\nb\"")]
    public void InternetCalendarProperty_RejectsAParameterThatCannotBeWritten(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => new InternetCalendarProperty("X-A", [new(name, value)], "value"));
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
            Created = new DateTime(2023, 12, 01, 10, 00, 00, kind),
            LastModified = new DateTime(2023, 12, 02, 10, 00, 00, kind),
            DateTimeStamp = new DateTime(2023, 12, 03, 10, 00, 00, kind),
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
        Assert.Empty(@event.RawProperties);
    }

    [Theory]
    [InlineData("GEO:37.386013;-122.082932")]
    [InlineData("CATEGORIES:WORK,MEETING")]
    [InlineData("URL:http://example.com/?a=1;b=2")]
    [InlineData("X-ESCAPE:a\\Nb")]
    [InlineData("X-TRAILING-BACKSLASH:a\\")]
    [InlineData("EXDATE;TZID=Europe/Paris:20240104T100000")]
    [InlineData("X-QUOTED;X-P=\"a:b;c\";X-LIST=\"x\",y:value")]
    public void Parse_KeepsAnUnknownPropertyThatIsNotASingleTextValueVerbatim(string contentLine)
    {
        var calendar = InternetCalendar.Parse(CreateIcs("UID:event-1", contentLine));

        var @event = Assert.Single(calendar.Events);
        Assert.Empty(@event.AdditionalProperties);
        Assert.Single(@event.RawProperties);
        Assert.Equal(1, CountContentLines(calendar.ToIcs(), contentLine));
    }

    [Fact]
    public void Parse_KeepsTheNameParametersAndValueOfAnUnknownProperty()
    {
        var property = Assert.Single(ParseSingleEvent("EXDATE;TZID=Europe/Paris;X-Q=\"a:b\":20240104T100000").RawProperties);

        Assert.Equal("EXDATE", property.Name);
        Assert.Equal([new("TZID", "Europe/Paris"), new("X-Q", "\"a:b\"")], property.Parameters);
        Assert.Equal("20240104T100000", property.Value);
    }

    [Fact]
    public void Parse_KeepsEveryOccurrenceOfARepeatedUnknownProperty()
    {
        var calendar = InternetCalendar.Parse(CreateIcs("X-A:1", "EXDATE:20240104T100000", "EXDATE:20240105T100000"));

        var @event = Assert.Single(calendar.Events);
        Assert.Equal(new[] { KeyValuePair.Create("X-A", "1") }, @event.AdditionalProperties);
        Assert.Equal(["20240104T100000", "20240105T100000"], @event.RawProperties.Select(p => p.Value));

        var ics = calendar.ToIcs();
        Assert.Equal(1, CountContentLines(ics, "EXDATE:20240104T100000"));
        Assert.Equal(1, CountContentLines(ics, "EXDATE:20240105T100000"));
    }

    [Fact]
    public void Parse_StoresAnUnknownPropertyWhoseTextValueRoundTripsAsAnAdditionalProperty()
    {
        var calendar = InternetCalendar.Parse(CreateIcs(@"X-A:a\, b\; c\\ d\ne"));

        var @event = Assert.Single(calendar.Events);
        Assert.Equal(new[] { KeyValuePair.Create("X-A", "a, b; c\\ d\ne") }, @event.AdditionalProperties);
        Assert.Empty(@event.RawProperties);
        Assert.Equal(1, CountContentLines(calendar.ToIcs(), @"X-A:a\, b\; c\\ d\ne"));
    }

    [Fact]
    public void Parse_KeepsAStructuredCalendarPropertyVerbatim()
    {
        var calendar = InternetCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nX-WR-CALNAME;VALUE=TEXT:Work\r\nX-LIST:a,b\r\nEND:VCALENDAR\r\n");

        Assert.Empty(calendar.AdditionalProperties);
        Assert.Equal(["X-WR-CALNAME", "X-LIST"], calendar.RawProperties.Select(p => p.Name));

        var ics = calendar.ToIcs();
        Assert.Equal(1, CountContentLines(ics, "X-WR-CALNAME;VALUE=TEXT:Work"));
        Assert.Equal(1, CountContentLines(ics, "X-LIST:a,b"));
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
    public void Parse_LeavesTheStatusUnsetWhenTheEventHasNone()
    {
        var calendar = InternetCalendar.Parse(CreateIcs("UID:event-1"));

        Assert.Null(Assert.Single(calendar.Events).Status);
        Assert.DoesNotContain("STATUS", calendar.ToIcs());
    }

    [Fact]
    public void Parse_ReadsALeapSecondAsTheLastSecondOfTheMinute()
    {
        var @event = ParseSingleEvent("DTSTART:20241231T235960Z", "DTEND:20241231T235960");

        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc), @event.Start);
        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Unspecified), @event.End);
    }

    [Theory]
    [InlineData("/home/john")]
    [InlineData("C:\\Users\\john")]
    [InlineData("john@example.com")]
    public void Parse_RejectsACalendarUserAddressWithoutAScheme(string address)
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("ORGANIZER:" + address), out _, out var error));
        Assert.Contains("calendar user address", error);
    }

    [Theory]
    [InlineData("MAILTO:john@example.com")]
    [InlineData("urn:uuid:f81d4fae-7dec-11d0-a765-00a0c91e6bf6")]
    [InlineData("https://example.com/users/john")]
    public void Parse_AcceptsACalendarUserAddressWithAScheme(string address)
    {
        Assert.NotNull(ParseSingleEvent("ATTENDEE:" + address).Attendees[0].Address);
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
    public void Parse_ReadsAnAllDayEventAndWritesItBackWithDateValues()
    {
        var calendar = InternetCalendar.Parse(CreateIcs("DTSTART;VALUE=DATE:20240101", "DTEND;VALUE=DATE:20240102"));

        var @event = Assert.Single(calendar.Events);
        Assert.True(@event.IsAllDay);
        Assert.Equal(new DateTime(2024, 01, 01), @event.Start);
        Assert.Equal(new DateTime(2024, 01, 02), @event.End);

        var ics = calendar.ToIcs();
        Assert.Equal("DTSTART;VALUE=DATE:20240101", GetContentLine(ics, "DTSTART"));
        Assert.Equal("DTEND;VALUE=DATE:20240102", GetContentLine(ics, "DTEND"));
    }

    [Fact]
    public void Parse_ReadsADateWithoutTheValueParameterAsAnAllDayEvent()
    {
        var @event = ParseSingleEvent("DTSTART:20240101", "DTEND:20240102");

        Assert.True(@event.IsAllDay);
        Assert.Equal(new DateTime(2024, 01, 01), @event.Start);
    }

    [Fact]
    public void Parse_ReadsADateTimeAsNotAllDay()
    {
        Assert.False(ParseSingleEvent("DTSTART:20240101T080000").IsAllDay);
    }

    [Fact]
    public void Parse_DoesNotApplyATimeZoneToADateValue()
    {
        var @event = ParseSingleEvent("DTSTART;VALUE=DATE;TZID=UTC:20240101", "DTEND;VALUE=DATE;TZID=UTC:20240102");

        Assert.True(@event.IsAllDay);
        Assert.Null(@event.TimeZone);
        Assert.Equal(DateTimeKind.Unspecified, @event.Start.Kind);
    }

    [Fact]
    public void Parse_RejectsADateValueThatIsNotADate()
    {
        Assert.False(InternetCalendar.TryParse(CreateIcs("DTSTART;VALUE=DATE:20240101T080000"), out _, out var error));
        Assert.Contains("not a date", error);
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
    public void Parse_ReadsATimeZoneThatIsNotFoundAsFloating()
    {
        // Rejecting the whole calendar would lose every other event; a floating value keeps the wall-clock reading.
        var @event = ParseSingleEvent("DTSTART;TZID=Nowhere/Unknown:20240102T080000", "DTEND;TZID=Nowhere/Unknown:20240102T090000");

        Assert.Null(@event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Unspecified), @event.Start);
        Assert.Equal(DateTimeKind.Unspecified, @event.Start.Kind);
        Assert.Equal(new DateTime(2024, 01, 02, 09, 00, 00, DateTimeKind.Unspecified), @event.End);
    }

    private static string CreateIcsWithTimeZone(string[] timeZoneContentLines, params string[] eventContentLines)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Test//Test//EN", "BEGIN:VTIMEZONE" };
        lines.AddRange(timeZoneContentLines);
        lines.Add("END:VTIMEZONE");
        lines.Add("BEGIN:VEVENT");
        lines.AddRange(eventContentLines);
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static DateTime ToUtc(Event @event, DateTime wallClock)
    {
        Assert.NotNull(@event.TimeZone);
        return TimeZoneInfo.ConvertTimeToUtc(wallClock, @event.TimeZone);
    }

    // Outlook writes a custom time zone anchored in 1601, whose DTSTART does not follow its RRULE.
    private static readonly string[] OutlookTimeZone =
    [
        "TZID:Customized Time Zone",
        "BEGIN:STANDARD",
        "DTSTART:16010101T030000",
        "TZOFFSETFROM:+0200",
        "TZOFFSETTO:+0100",
        "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10",
        "END:STANDARD",
        "BEGIN:DAYLIGHT",
        "DTSTART:16010101T020000",
        "TZOFFSETFROM:+0100",
        "TZOFFSETTO:+0200",
        "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3",
        "END:DAYLIGHT",
    ];

    [Fact]
    public void Parse_BuildsTheTimeZoneOfAnOutlookVTimeZone()
    {
        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(OutlookTimeZone, "DTSTART;TZID=\"Customized Time Zone\":20240715T090000")).Events);

        Assert.Equal("Customized Time Zone", @event.TimeZone?.Id);
        Assert.Equal(new DateTime(2024, 07, 15, 09, 00, 00, DateTimeKind.Unspecified), @event.Start);
        Assert.Equal(new DateTime(2024, 07, 15, 07, 00, 00, DateTimeKind.Utc), ToUtc(@event, @event.Start));
        Assert.Equal(new DateTime(2024, 01, 15, 08, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 01, 15, 09, 00, 00)));

        // The last Sunday of March 2024 is the 31st and the last Sunday of October 2024 the 27th
        Assert.Equal(new DateTime(2024, 03, 31, 00, 59, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 03, 31, 01, 59, 00)));
        Assert.Equal(new DateTime(2024, 03, 31, 01, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 03, 31, 03, 00, 00)));
        Assert.Equal(new DateTime(2024, 03, 24, 08, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 03, 24, 09, 00, 00)));
        Assert.Equal(new DateTime(2024, 10, 26, 07, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 10, 26, 09, 00, 00)));
        Assert.Equal(new DateTime(2024, 10, 27, 08, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 10, 27, 09, 00, 00)));

        // The identifier is kept, so the event is written back with the TZID it was read with
        Assert.Equal("DTSTART;TZID=Customized Time Zone:20240715T090000", GetContentLine(CreateCalendarWithEvent(@event).ToIcs(), "DTSTART"));
    }

    [Theory]
    [InlineData(1700, 4, 5)]
    [InlineData(2026, 5, 6)]
    [InlineData(2029, 8, 2)]
    [InlineData(2400, 2, 3)]
    [InlineData(9000, 6, 7)]
    public void Parse_FollowsAnOpenEndedVTimeZoneRecurrenceWithoutTransitionTimeForAnyYear(int year, int aprilDay, int septemberDay)
    {
        // Chile changes on the first Sunday after the first Saturday, which no TimeZoneInfo.TransitionTime expresses, and Outlook
        // anchors the recurrences in 1601.
        string[] timeZone =
        [
            "TZID:Chile",
            "BEGIN:STANDARD",
            "DTSTART:16010101T000000",
            "TZOFFSETFROM:-0300",
            "TZOFFSETTO:-0400",
            "RRULE:FREQ=YEARLY;BYMONTH=4;BYDAY=SU;BYMONTHDAY=2,3,4,5,6,7,8",
            "END:STANDARD",
            "BEGIN:DAYLIGHT",
            "DTSTART:16010101T000000",
            "TZOFFSETFROM:-0400",
            "TZOFFSETTO:-0300",
            "RRULE:FREQ=YEARLY;BYMONTH=9;BYDAY=SU;BYMONTHDAY=2,3,4,5,6,7,8",
            "END:DAYLIGHT",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Chile:20260715T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        foreach (var (utc, offset) in new[]
        {
            (new DateTime(year, 01, 15, 12, 00, 00, DateTimeKind.Utc), -3),
            (new DateTime(year, 04, aprilDay, 02, 59, 59, DateTimeKind.Utc), -3),
            (new DateTime(year, 04, aprilDay, 03, 00, 00, DateTimeKind.Utc), -4),
            (new DateTime(year, 07, 15, 16, 00, 00, DateTimeKind.Utc), -4),
            (new DateTime(year, 09, septemberDay, 03, 59, 59, DateTimeKind.Utc), -4),
            (new DateTime(year, 09, septemberDay, 04, 00, 00, DateTimeKind.Utc), -3),
            (new DateTime(year, 12, 31, 23, 30, 00, DateTimeKind.Utc), -3),
        })
        {
            Assert.Equal(TimeSpan.FromHours(offset), @event.TimeZone.GetUtcOffset(utc), $"{utc:O}");
        }
    }

    [Fact]
    public void Parse_BuildsTheTimeZoneOfAMozillaVTimeZone()
    {
        const string Id = "/mozilla.org/20050126_1/America/New_York";
        string[] timeZone =
        [
            "TZID:" + Id,
            "X-LIC-LOCATION:America/New_York",
            "BEGIN:DAYLIGHT",
            "TZOFFSETFROM:-0500",
            "TZOFFSETTO:-0400",
            "TZNAME:EDT",
            "DTSTART:19700308T020000",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "TZOFFSETFROM:-0400",
            "TZOFFSETTO:-0500",
            "TZNAME:EST",
            "DTSTART:19701101T020000",
            "RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU",
            "END:STANDARD",
        ];

        var calendar = InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=" + Id + ":20240310T013000", "DTEND;TZID=" + Id + ":20240310T033000"));

        var @event = Assert.Single(calendar.Events);
        Assert.NotNull(@event.TimeZone);
        Assert.Equal(Id, @event.TimeZone.Id);
        Assert.Equal(new DateTime(2024, 03, 10, 06, 30, 00, DateTimeKind.Utc), ToUtc(@event, @event.Start));
        Assert.Equal(new DateTime(2024, 03, 10, 07, 30, 00, DateTimeKind.Utc), ToUtc(@event, @event.End));
        Assert.Equal(new DateTime(2024, 11, 03, 06, 30, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 11, 03, 01, 30, 00)));
        Assert.Equal(new DateTime(2024, 11, 03, 07, 30, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 11, 03, 02, 30, 00)));
        Assert.Equal(new DateTime(2024, 11, 02, 13, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 11, 02, 09, 00, 00)));
        Assert.Equal("EST", @event.TimeZone.StandardName);
        Assert.Equal("EDT", @event.TimeZone.DaylightName);

        Assert.Equal("DTSTART;TZID=" + Id + ":20240310T013000", GetContentLine(calendar.ToIcs(), "DTSTART"));
    }

    [Fact]
    public void Parse_BuildsAFixedDateTransitionFromAVTimeZone()
    {
        string[] timeZone =
        [
            "TZID:Test/Tehran",
            "BEGIN:STANDARD",
            "DTSTART:19700922T000000",
            "TZOFFSETFROM:+0430",
            "TZOFFSETTO:+0330",
            "RRULE:FREQ=YEARLY;BYMONTH=9;BYMONTHDAY=22",
            "END:STANDARD",
            "BEGIN:DAYLIGHT",
            "DTSTART:19700322T000000",
            "TZOFFSETFROM:+0330",
            "TZOFFSETTO:+0430",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=22",
            "END:DAYLIGHT",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Tehran:20210601T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal("Test/Tehran", @event.TimeZone.Id);
        var rule = Assert.Single(@event.TimeZone.GetAdjustmentRules());
        Assert.True(rule.DaylightTransitionStart.IsFixedDateRule);
        Assert.Equal(new DateTime(2021, 06, 01, 07, 30, 00, DateTimeKind.Utc), ToUtc(@event, @event.Start));
        Assert.Equal(new DateTime(2021, 12, 01, 08, 30, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2021, 12, 01, 12, 00, 00)));
        Assert.Equal(new DateTime(2021, 03, 21, 08, 30, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2021, 03, 21, 12, 00, 00)));
        Assert.Equal(new DateTime(2021, 03, 22, 07, 30, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2021, 03, 22, 12, 00, 00)));
    }

    [Fact]
    public void Parse_BuildsTheHistoricalTransitionsOfAVTimeZone()
    {
        // The rules the United States followed from 1987 to 2006, then the current ones.
        string[] timeZone =
        [
            "TZID:Test/Eastern",
            "BEGIN:DAYLIGHT",
            "TZOFFSETFROM:-0500",
            "RRULE:FREQ=YEARLY;UNTIL=20060402T070000Z;BYMONTH=4;BYDAY=1SU",
            "DTSTART:20000402T020000",
            "TZNAME:EDT",
            "TZOFFSETTO:-0400",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "TZOFFSETFROM:-0400",
            "RRULE:FREQ=YEARLY;UNTIL=20061029T060000Z;BYMONTH=10;BYDAY=-1SU",
            "DTSTART:20001029T020000",
            "TZNAME:EST",
            "TZOFFSETTO:-0500",
            "END:STANDARD",
            "BEGIN:DAYLIGHT",
            "TZOFFSETFROM:-0500",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU",
            "DTSTART:20070311T020000",
            "TZNAME:EDT",
            "TZOFFSETTO:-0400",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "TZOFFSETFROM:-0400",
            "RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU",
            "DTSTART:20071104T020000",
            "TZNAME:EST",
            "TZOFFSETTO:-0500",
            "END:STANDARD",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Eastern:20050320T120000")).Events);

        // In 2005, daylight saving time ran from April 3 to October 30; the current rules would say March 13 to November 6
        Assert.Equal(new DateTime(2005, 03, 20, 17, 00, 00, DateTimeKind.Utc), ToUtc(@event, @event.Start));
        Assert.Equal(new DateTime(2005, 04, 10, 16, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2005, 04, 10, 12, 00, 00)));
        Assert.Equal(new DateTime(2005, 10, 29, 16, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2005, 10, 29, 12, 00, 00)));
        Assert.Equal(new DateTime(2005, 11, 01, 17, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2005, 11, 01, 12, 00, 00)));
        Assert.Equal(new DateTime(2024, 03, 20, 16, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 03, 20, 12, 00, 00)));
        Assert.Equal(new DateTime(2024, 11, 04, 17, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2024, 11, 04, 12, 00, 00)));
    }

    [Fact]
    public void Parse_BuildsTheTransitionsOfAVTimeZoneListingTheirDates()
    {
        // A zone that stopped observing daylight saving time, described by RDATEs as full time zone databases do.
        string[] timeZone =
        [
            "TZID:Test/Dates",
            "BEGIN:DAYLIGHT",
            "DTSTART:20100328T020000",
            "RDATE:20110327T020000",
            "TZOFFSETFROM:+0100",
            "TZOFFSETTO:+0200",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "DTSTART:20101031T030000",
            "RDATE:20111030T030000",
            "TZOFFSETFROM:+0200",
            "TZOFFSETTO:+0100",
            "END:STANDARD",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Dates:20100701T120000")).Events);

        Assert.Equal(TimeSpan.FromHours(1), @event.TimeZone?.BaseUtcOffset);
        Assert.Equal(new DateTime(2010, 07, 01, 10, 00, 00, DateTimeKind.Utc), ToUtc(@event, @event.Start));
        Assert.Equal(new DateTime(2010, 12, 01, 11, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2010, 12, 01, 12, 00, 00)));
        Assert.Equal(new DateTime(2011, 07, 01, 10, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2011, 07, 01, 12, 00, 00)));
        Assert.Equal(new DateTime(2012, 07, 01, 11, 00, 00, DateTimeKind.Utc), ToUtc(@event, new DateTime(2012, 07, 01, 12, 00, 00)));
    }

    [Fact]
    public void Parse_BuildsAChangeOfTheStandardOffsetFromAVTimeZone()
    {
        // Almaty moved from +06 to +05 on 2024-03-01, which is not a daylight saving time transition.
        string[] timeZone =
        [
            "TZID:Test/Almaty",
            "BEGIN:STANDARD",
            "DTSTART:20240301T000000",
            "TZOFFSETFROM:+0600",
            "TZOFFSETTO:+0500",
            "END:STANDARD",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Almaty:20240115T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal(TimeSpan.FromHours(6), @event.TimeZone.GetUtcOffset(new DateTime(2024, 02, 29, 17, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(5), @event.TimeZone.GetUtcOffset(new DateTime(2024, 02, 29, 18, 00, 00, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(6), @event.TimeZone.GetUtcOffset(new DateTime(2023, 12, 31, 18, 30, 00, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(5), @event.TimeZone.GetUtcOffset(new DateTime(2030, 01, 01, 00, 00, 00, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("+0500", "+0600", 5, 6)]
    [InlineData("-0500", "-0600", -5, -6)]
    [InlineData("+0600", "+0500", 6, 5)]
    [InlineData("-0600", "-0500", -6, -5)]
    public void Parse_BuildsAChangeOfTheOffsetAtMidnightOnJanuaryFirstFromAVTimeZone(string offsetFrom, string offsetTo, int hoursFrom, int hoursTo)
    {
        // The change happens at midnight in the offset in effect before it, as in Asia/Hovd in 1978 or Indian/Chagos in 1996.
        string[] timeZone = ["TZID:Test/NewYear", "BEGIN:STANDARD", "DTSTART:20270101T000000", "TZOFFSETFROM:" + offsetFrom, "TZOFFSETTO:" + offsetTo, "END:STANDARD"];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/NewYear:20260715T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        var change = new DateTime(2027, 01, 01, 00, 00, 00, DateTimeKind.Utc).AddHours(-hoursFrom);
        foreach (var delta in new[] { TimeSpan.FromHours(-2), TimeSpan.FromHours(-1), TimeSpan.FromMinutes(-1), TimeSpan.FromSeconds(-1) })
        {
            Assert.Equal(TimeSpan.FromHours(hoursFrom), @event.TimeZone.GetUtcOffset(change + delta), $"{change + delta:O}");
        }

        foreach (var delta in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromDays(400) })
        {
            Assert.Equal(TimeSpan.FromHours(hoursTo), @event.TimeZone.GetUtcOffset(change + delta), $"{change + delta:O}");
        }

        Assert.Equal(TimeSpan.FromHours(hoursFrom), @event.TimeZone.GetUtcOffset(change.AddDays(-400)));
    }

    [Fact]
    public void Parse_BuildsAChangeOfTheOffsetAtMidnightOnJanuaryFirstOfTheSecondYearFromAVTimeZone()
    {
        // A daylight saving period starting with one of the first years makes some runtimes throw when computing the offsets.
        string[] timeZone = ["TZID:Test/SecondYear", "BEGIN:STANDARD", "DTSTART:00020101T000000", "TZOFFSETFROM:+0700", "TZOFFSETTO:+0800", "END:STANDARD"];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/SecondYear:20260715T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal(TimeSpan.FromHours(8), @event.TimeZone.GetUtcOffset(new DateTime(2026, 07, 15, 00, 00, 00, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(8), @event.TimeZone.GetUtcOffset(new DateTime(0003, 07, 15, 00, 00, 00, DateTimeKind.Utc)));
    }

    [Fact]
    public void Parse_BuildsADaylightSavingPeriodSpanningTheEndOfTheYearFromAVTimeZone()
    {
        // As a writer anchoring the sub-components in the year of the events does, the first onset of 2024 ends the daylight
        // saving period that started in 2023.
        string[] timeZone =
        [
            "TZID:Test/Santiago",
            "BEGIN:STANDARD",
            "DTSTART:20240406T235959",
            "TZOFFSETFROM:-0300",
            "TZOFFSETTO:-0400",
            "RDATE:20250405T235959",
            "END:STANDARD",
            "BEGIN:DAYLIGHT",
            "DTSTART:20240907T235959",
            "TZOFFSETFROM:-0400",
            "TZOFFSETTO:-0300",
            "RDATE:20250906T235959",
            "END:DAYLIGHT",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Santiago:20240115T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        foreach (var (utc, offset) in new[]
        {
            (new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc), -3),
            (new DateTime(2024, 01, 01, 03, 30, 00, DateTimeKind.Utc), -3),
            (new DateTime(2024, 04, 07, 02, 59, 58, DateTimeKind.Utc), -3),
            (new DateTime(2024, 04, 07, 02, 59, 59, DateTimeKind.Utc), -4),
            (new DateTime(2024, 09, 08, 03, 59, 58, DateTimeKind.Utc), -4),
            (new DateTime(2024, 09, 08, 03, 59, 59, DateTimeKind.Utc), -3),
            (new DateTime(2024, 12, 31, 23, 30, 00, DateTimeKind.Utc), -3),
            (new DateTime(2025, 01, 01, 02, 30, 00, DateTimeKind.Utc), -3),
            (new DateTime(2025, 04, 06, 03, 00, 00, DateTimeKind.Utc), -4),
            (new DateTime(2025, 09, 07, 04, 00, 00, DateTimeKind.Utc), -3),
            (new DateTime(2026, 06, 01, 00, 00, 00, DateTimeKind.Utc), -3),
        })
        {
            Assert.Equal(TimeSpan.FromHours(offset), @event.TimeZone.GetUtcOffset(utc), $"{utc:O}");
        }
    }

    [Fact]
    public void Parse_ApproximatesAVTimeZoneWhoseOffsetChangesByADay()
    {
        // Samoa skipped 2011-12-30 by moving from -10 to +14. An adjustment rule cannot change the offset by 24 hours, so the
        // year keeps its longest offset rather than the whole time zone being rejected.
        string[] timeZone =
        [
            "TZID:Test/Apia",
            "BEGIN:DAYLIGHT",
            "DTSTART:20111229T235959",
            "TZOFFSETFROM:-1000",
            "TZOFFSETTO:+1400",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "DTSTART:20120401T040000",
            "TZOFFSETFROM:+1400",
            "TZOFFSETTO:+1300",
            "END:STANDARD",
        ];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Apia:20120115T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal(TimeSpan.FromHours(-10), @event.TimeZone.GetUtcOffset(new DateTime(2011, 06, 01, 00, 00, 00, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(14), @event.TimeZone.GetUtcOffset(new DateTime(2012, 02, 01, 00, 00, 00, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(13), @event.TimeZone.GetUtcOffset(new DateTime(2012, 06, 01, 00, 00, 00, DateTimeKind.Utc)));
    }

    [Fact]
    public void Parse_ReadsAHostileVTimeZoneInBoundedTime()
    {
        string[] timeZone =
        [
            "TZID:Test/Hostile",
            "BEGIN:DAYLIGHT",
            "DTSTART:00010101T020000",
            "TZOFFSETFROM:+0100",
            "TZOFFSETTO:+0200",
            "RRULE:FREQ=YEARLY;BYMONTH=1,2,3,4,5,6,7,8,9,10,11,12;BYMONTHDAY=1,2,3,4,5,6,7,8,9,10,11,12,13,14,15;UNTIL=99991231T000000Z",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "DTSTART:00010101T030000",
            "TZOFFSETFROM:+0200",
            "TZOFFSETTO:+0100",
            "RRULE:FREQ=YEARLY;BYMONTH=1,2,3,4,5,6,7,8,9,10,11,12;BYMONTHDAY=16,17,18,19,20,21,22,23,24,25,26,27,28;COUNT=100000",
            "END:STANDARD",
        ];

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(InternetCalendar.TryParse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Hostile:20240115T120000"), out _, out var error), error);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1), stopwatch.Elapsed.ToString());
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    [Theory]
    [InlineData("Asia/Almaty", 2024)]
    [InlineData("America/Santiago", 2026)]
    [InlineData("Africa/Casablanca", 2024)]
    [InlineData("America/Scoresbysund", 2024)]
    [InlineData("America/Sao_Paulo", 2010)]
    [InlineData("Europe/Moscow", 2010)]
    [InlineData("Australia/Lord_Howe", 2024)]
    public void Parse_ReadsTheVTimeZoneWrittenForASystemTimeZone(string id, int year)
    {
        // An IANA identifier does not resolve on Windows when globalization is invariant. Renaming the identifier makes the
        // parser build the time zone from the VTIMEZONE instead of finding the system one.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var systemTimeZone))
        {
            global::Xunit.Assert.Skip($"The time zone '{id}' is not available on this machine.");
            return;
        }

        var start = new DateTime(year, 01, 15, 12, 00, 00);
        var calendar = CreateCalendarWithEvent(new Event { Start = start, End = start.AddHours(1), TimeZone = systemTimeZone });
        var ics = calendar.ToIcs()
            .Replace("\r\n ", "", StringComparison.Ordinal)
            .Replace("TZID:" + id + "\r\n", "TZID:Test/Renamed\r\n", StringComparison.Ordinal)
            .Replace("TZID=" + id + ":", "TZID=Test/Renamed:", StringComparison.Ordinal);

        var @event = Assert.Single(InternetCalendar.Parse(ics).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal("Test/Renamed", @event.TimeZone.Id);

        // A year changing its offset more than twice cannot be expressed by an adjustment rule, which the time zone database
        // of some machines gives Morocco in 2026, so two years are compared.
        for (var instant = new DateTime(year, 01, 01, 00, 00, 00, DateTimeKind.Utc); instant < new DateTime(year + 2, 01, 01, 00, 00, 00, DateTimeKind.Utc); instant = instant.AddMinutes(30))
        {
            Assert.Equal(systemTimeZone.GetUtcOffset(instant), @event.TimeZone.GetUtcOffset(instant), $"{instant:O}");
        }
    }
#endif

    [Fact]
    public void Parse_BuildsAFixedOffsetTimeZoneFromAVTimeZoneWithoutDaylightSavingTime()
    {
        string[] timeZone = ["TZID:Test/Kolkata", "BEGIN:STANDARD", "DTSTART:19700101T000000", "TZOFFSETFROM:+0530", "TZOFFSETTO:+0530", "END:STANDARD"];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Kolkata:20240101T120000")).Events);

        Assert.NotNull(@event.TimeZone);
        Assert.Equal(TimeSpan.FromMinutes(330), @event.TimeZone.BaseUtcOffset);
        Assert.Empty(@event.TimeZone.GetAdjustmentRules());
    }

    [Fact]
    public void Parse_UsesAVTimeZoneThatFollowsTheEvent()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\nDTSTART;TZID=Test/Kolkata:20240101T120000\r\nEND:VEVENT\r\n" +
            "BEGIN:VTIMEZONE\r\nTZID:Test/Kolkata\r\nBEGIN:STANDARD\r\nDTSTART:19700101T000000\r\nTZOFFSETFROM:+0530\r\nTZOFFSETTO:+0530\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n" +
            "END:VCALENDAR\r\n";

        var @event = Assert.Single(InternetCalendar.Parse(ics).Events);

        Assert.Equal("Test/Kolkata", @event.TimeZone?.Id);
    }

    [Fact]
    public void Parse_PrefersTheSystemTimeZoneToTheVTimeZone()
    {
        string[] timeZone = ["TZID:UTC", "BEGIN:STANDARD", "DTSTART:19700101T000000", "TZOFFSETFROM:+0500", "TZOFFSETTO:+0500", "END:STANDARD"];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=UTC:20240101T120000")).Events);

        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
    }

    [Fact]
    public void Parse_ReadsAnInvalidVTimeZoneAsAnUnresolvedTimeZone()
    {
        string[] timeZone = ["TZID:Test/Invalid", "BEGIN:STANDARD", "DTSTART:19700101T000000", "TZOFFSETFROM:+0500", "TZOFFSETTO:not-an-offset", "END:STANDARD"];

        var @event = Assert.Single(InternetCalendar.Parse(CreateIcsWithTimeZone(timeZone, "DTSTART;TZID=Test/Invalid:20240101T120000")).Events);

        Assert.Null(@event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 01, 12, 00, 00, DateTimeKind.Unspecified), @event.Start);
    }

    [Fact]
    public void Parse_ResolvesThePlatformTimeZoneEndingAPrefixedIdentifier()
    {
        var @event = ParseSingleEvent("DTSTART;TZID=/softwarestudio.org/Tzfile/UTC:20240102T080000");

        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    [Fact]
    public void Parse_ResolvesTheIanaTimeZoneEndingAMozillaIdentifier()
    {
        // An IANA identifier does not resolve on Windows when globalization is invariant.
        var @event = ParseSingleEvent("DTSTART;TZID=/mozilla.org/20050126_1/America/New_York:20240102T080000");

        Assert.NotNull(@event.TimeZone);
        Assert.Equal(TimeSpan.FromHours(-5), @event.TimeZone.GetUtcOffset(@event.Start));
    }
#endif

    [Fact]
    public void Parse_ConvertsATimeStampCarryingATzidToUtcWithoutChangingTheEventTimeZone()
    {
        string[] timeZone = ["TZID:Test/Tokyo", "BEGIN:STANDARD", "DTSTART:19700101T000000", "TZOFFSETFROM:+0900", "TZOFFSETTO:+0900", "END:STANDARD"];

        var calendar = InternetCalendar.Parse(CreateIcsWithTimeZone(
            timeZone,
            "DTSTART:20240102T080000",
            "DTEND:20240102T090000",
            "LAST-MODIFIED;TZID=Test/Tokyo:20240102T090000",
            "CREATED;TZID=Nowhere/Unknown:20240101T090000",
            "DTSTAMP:20240103T090000"));

        var @event = Assert.Single(calendar.Events);
        Assert.Null(@event.TimeZone);
        Assert.Equal(new DateTime(2024, 01, 02, 00, 00, 00, DateTimeKind.Utc), @event.LastModified);
        Assert.Equal(DateTimeKind.Utc, @event.LastModified.Kind);

        // A floating value, or one whose time zone is unknown, is taken as UTC as RFC 5545 requires these properties to be
        Assert.Equal(new DateTime(2024, 01, 01, 09, 00, 00, DateTimeKind.Utc), @event.Created);
        Assert.Equal(DateTimeKind.Utc, @event.Created.Kind);
        Assert.Equal(new DateTime(2024, 01, 03, 09, 00, 00, DateTimeKind.Utc), @event.DateTimeStamp);

        var ics = calendar.ToIcs();
        Assert.Equal("DTSTART:20240102T080000", GetContentLine(ics, "DTSTART"));
        Assert.Equal("LAST-MODIFIED:20240102T000000Z", GetContentLine(ics, "LAST-MODIFIED"));
    }

    [Fact]
    public void Parse_DoesNotRejectAnEventWhoseTimeStampUsesAnotherTimeZone()
    {
        var @event = ParseSingleEvent("DTSTART;TZID=UTC:20240102T080000", "DTSTAMP;TZID=Nowhere/Unknown:20240102T080000");

        Assert.Equal(TimeZoneInfo.Utc, @event.TimeZone);
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

    private static string WriteVTimeZone(TimeZoneInfo timeZone, DateTime start)
    {
        var @event = CreateEvent();
        @event.Start = start;
        @event.End = start.AddHours(1);
        @event.TimeZone = timeZone;

        // The assertions read whole content lines, so the long RRULE and RDATE lines are unfolded first
        return CreateCalendarWithEvent(@event).ToIcs().Replace("\r\n ", "", StringComparison.Ordinal);
    }

    private static DateTime GetFirstDayOfWeekOnOrAfter(DateTime date, DayOfWeek dayOfWeek)
    {
        return date.AddDays(((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7);
    }

    // Unix materializes the transitions of each year as fixed-date rules, and Windows does the same for a time zone
    // whose rule changes every year. The transitions are given as local dates and times.
    private static TimeZoneInfo CreateTimeZoneWithYearlyRules(string id, TimeSpan baseUtcOffset, int firstYear, int lastYear, Func<int, DateTime> daylightStart, Func<int, DateTime> daylightEnd)
    {
        var rules = new List<TimeZoneInfo.AdjustmentRule>();
        for (var year = firstYear; year <= lastYear; year++)
        {
            var start = daylightStart(year);
            var end = daylightEnd(year);
            rules.Add(TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(year, 1, 1),
                new DateTime(year, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1) + start.TimeOfDay, start.Month, start.Day),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1) + end.TimeOfDay, end.Month, end.Day)));
        }

        return TimeZoneInfo.CreateCustomTimeZone(id, baseUtcOffset, id, "STD", "DST", [.. rules]);
    }

    /// <summary>Expands the transitions of the VTIMEZONE sub-components up to the end of <paramref name="lastYear"/>, as UTC instants.</summary>
    private static List<(DateTime Utc, TimeSpan From, TimeSpan To)> GetVTimeZoneOnsets(string ics, int lastYear)
    {
        var onsets = new List<(DateTime Utc, TimeSpan From, TimeSpan To)>();
        var inTimeZone = false;
        var start = DateTime.MinValue;
        var from = TimeSpan.Zero;
        var to = TimeSpan.Zero;
        string? recurrenceRule = null;
        var recurrenceDates = new List<DateTime>();
        foreach (var contentLine in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (contentLine is "BEGIN:VTIMEZONE")
            {
                inTimeZone = true;
            }
            else if (contentLine is "END:VTIMEZONE")
            {
                break;
            }
            else if (!inTimeZone || contentLine.StartsWith("TZID:", StringComparison.Ordinal))
            {
                continue;
            }
            else if (contentLine is "BEGIN:STANDARD" or "BEGIN:DAYLIGHT")
            {
                recurrenceRule = null;
                recurrenceDates.Clear();
            }
            else if (contentLine.StartsWith("DTSTART:", StringComparison.Ordinal))
            {
                start = DateTime.ParseExact(contentLine["DTSTART:".Length..], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            }
            else if (contentLine.StartsWith("RDATE:", StringComparison.Ordinal))
            {
                recurrenceDates.Add(DateTime.ParseExact(contentLine["RDATE:".Length..], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
            }
            else if (contentLine.StartsWith("TZOFFSETFROM:", StringComparison.Ordinal))
            {
                from = ParseUtcOffset(contentLine["TZOFFSETFROM:".Length..]);
            }
            else if (contentLine.StartsWith("TZOFFSETTO:", StringComparison.Ordinal))
            {
                to = ParseUtcOffset(contentLine["TZOFFSETTO:".Length..]);
            }
            else if (contentLine.StartsWith("RRULE:", StringComparison.Ordinal))
            {
                recurrenceRule = contentLine["RRULE:".Length..];
            }
            else if (contentLine is "END:STANDARD" or "END:DAYLIGHT")
            {
                // The DTSTART and RDATE of a sub-component are local times expressed in its TZOFFSETFROM offset.
                onsets.Add((start - from, from, to));
                foreach (var date in recurrenceDates)
                {
                    onsets.Add((date - from, from, to));
                }

                if (recurrenceRule is not null)
                {
                    foreach (var occurrence in ExpandYearlyRecurrenceRule(recurrenceRule, start, from, lastYear))
                    {
                        onsets.Add((occurrence - from, from, to));
                    }
                }
            }
            else
            {
                Assert.Fail($"Unexpected content line '{contentLine}' in the VTIMEZONE:\n{ics}");
            }
        }

        onsets.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        return onsets;
    }

    private static TimeSpan ParseUtcOffset(string value)
    {
        var offset = new TimeSpan(
            int.Parse(value.AsSpan(1, 2), CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(3, 2), CultureInfo.InvariantCulture),
            value.Length > 5 ? int.Parse(value.AsSpan(5, 2), CultureInfo.InvariantCulture) : 0);
        return value[0] is '-' ? -offset : offset;
    }

    // Supports the yearly rules a VTIMEZONE writer produces, by testing every day of the year against each rule part
    private static IEnumerable<DateTime> ExpandYearlyRecurrenceRule(string recurrenceRule, DateTime start, TimeSpan from, int lastYear)
    {
        var parts = recurrenceRule.Split(';').Select(part => part.Split('=')).ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
        Assert.Equal("YEARLY", parts["FREQ"]);
        foreach (var name in parts.Keys)
        {
            Assert.Contains(name, (string[])["FREQ", "BYMONTH", "BYMONTHDAY", "BYYEARDAY", "BYDAY", "UNTIL"]);
        }

        DateTime? until = parts.TryGetValue("UNTIL", out var untilValue) ? DateTime.ParseExact(untilValue, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) : null;
        for (var year = start.Year; year <= lastYear; year++)
        {
            for (var day = new DateTime(year, 1, 1); day.Year == year; day = day.AddDays(1))
            {
                var occurrence = day + start.TimeOfDay;
                if (occurrence <= start || !IsIncludedByRecurrenceRule(parts, day))
                    continue;

                if (until is not null && occurrence - from > until.Value)
                    yield break;

                yield return occurrence;
            }
        }
    }

    private static bool IsIncludedByRecurrenceRule(Dictionary<string, string> parts, DateTime day)
    {
        var daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
        var daysInYear = DateTime.IsLeapYear(day.Year) ? 366 : 365;
        if (parts.TryGetValue("BYMONTH", out var months) && !ParseIntegers(months).Contains(day.Month))
            return false;

        if (parts.TryGetValue("BYMONTHDAY", out var monthDays) && !ParseIntegers(monthDays).Any(value => value > 0 ? value == day.Day : daysInMonth + value + 1 == day.Day))
            return false;

        if (parts.TryGetValue("BYYEARDAY", out var yearDays) && !ParseIntegers(yearDays).Any(value => value > 0 ? value == day.DayOfYear : daysInYear + value + 1 == day.DayOfYear))
            return false;

        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            // A single day of the week, whose ordinal counts the weeks of the month
            var dayOfWeek = byDay[^2..] switch
            {
                "SU" => DayOfWeek.Sunday,
                "MO" => DayOfWeek.Monday,
                "TU" => DayOfWeek.Tuesday,
                "WE" => DayOfWeek.Wednesday,
                "TH" => DayOfWeek.Thursday,
                "FR" => DayOfWeek.Friday,
                "SA" => DayOfWeek.Saturday,
                _ => throw new FormatException("Invalid BYDAY: " + byDay),
            };

            if (day.DayOfWeek != dayOfWeek)
                return false;

            if (byDay.Length > 2)
            {
                var ordinal = int.Parse(byDay.AsSpan(0, byDay.Length - 2), CultureInfo.InvariantCulture);
                var actualOrdinal = ordinal > 0 ? ((day.Day - 1) / 7) + 1 : -(((daysInMonth - day.Day) / 7) + 1);
                if (ordinal != actualOrdinal)
                    return false;
            }
        }

        return true;
    }

    private static int[] ParseIntegers(string value)
    {
        return value.Split(',').Select(item => int.Parse(item, CultureInfo.InvariantCulture)).ToArray();
    }

    private static void AssertVTimeZoneMatchesTimeZone(string ics, TimeZoneInfo timeZone, int firstYear, int lastYear)
    {
        var onsets = GetVTimeZoneOnsets(ics, lastYear);
        Assert.NotEmpty(onsets);

        var index = -1;
        for (var instant = new DateTime(firstYear, 1, 1, 0, 0, 0, DateTimeKind.Utc); instant.Year <= lastYear; instant = instant.AddMinutes(30))
        {
            while (index + 1 < onsets.Count && onsets[index + 1].Utc <= instant)
            {
                index++;
            }

            // Before its first onset, a time zone is at the offset that onset changes from
            var offset = index >= 0 ? onsets[index].To : onsets[0].From;
            var expected = timeZone.GetUtcOffset(instant);
            if (offset != expected)
            {
                Assert.Fail($"At {instant:yyyy-MM-ddTHH:mm}Z, the offset of '{timeZone.Id}' is {expected} but the VTIMEZONE describes {offset}:\n{ics}");
            }
        }
    }

    [Fact]
    public void ToIcs_WritesADaylightSavingTimeTransitionOnADayOfTheWeekWithinAWindowOfTheMonth()
    {
        // Israel starts daylight saving time on the Friday before the last Sunday of March, which is not the n-th
        // Friday of the month: it falls on 2024-03-29 (the fifth Friday) and on 2026-03-27 (the fourth).
        var timeZone = CreateTimeZoneWithYearlyRules(
            "Test/Jerusalem",
            TimeSpan.FromHours(2),
            firstYear: 2020,
            lastYear: 2030,
            year => GetFirstDayOfWeekOnOrAfter(new DateTime(year, 3, 23), DayOfWeek.Friday).AddHours(2),
            year => GetFirstDayOfWeekOnOrAfter(new DateTime(year, 10, 25), DayOfWeek.Sunday).AddHours(2));

        var ics = WriteVTimeZone(timeZone, new DateTime(2024, 01, 15, 12, 00, 00));

        var expected = string.Join("\r\n",
            "BEGIN:VTIMEZONE",
            "TZID:Test/Jerusalem",
            "BEGIN:DAYLIGHT",
            "DTSTART:20240329T020000",
            "TZOFFSETFROM:+0200",
            "TZOFFSETTO:+0300",
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=FR;BYMONTHDAY=23,24,25,26,27,28,29;UNTIL=20300329T000000Z",
            "END:DAYLIGHT",
            "BEGIN:STANDARD",
            "DTSTART:20241027T020000",
            "TZOFFSETFROM:+0300",
            "TZOFFSETTO:+0200",
            "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU;UNTIL=20301026T230000Z",
            "END:STANDARD",
            "END:VTIMEZONE") + "\r\n";

        Assert.Contains(expected, ics);
        AssertVTimeZoneMatchesTimeZone(ics, timeZone, 2024, 2032);
    }

    [Fact]
    public void ToIcs_WritesATransitionWhoseWindowSpillsOverTheNextMonthAsDaysOfTheYear()
    {
        // Egypt ends daylight saving time at midnight after the last Thursday of October, which is 2024-11-01.
        var timeZone = CreateTimeZoneWithYearlyRules(
            "Test/Cairo",
            TimeSpan.FromHours(2),
            firstYear: 2023,
            lastYear: 2030,
            year => GetFirstDayOfWeekOnOrAfter(new DateTime(year, 4, 24), DayOfWeek.Friday),
            year => GetFirstDayOfWeekOnOrAfter(new DateTime(year, 10, 26), DayOfWeek.Friday));

        var ics = WriteVTimeZone(timeZone, new DateTime(2024, 01, 15, 12, 00, 00));

        Assert.Contains("DTSTART:20241101T000000\r\n", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;BYYEARDAY=-67,-66,-65,-64,-63,-62,-61;BYDAY=FR;UNTIL=20301031T210000Z\r\n", ics);
        AssertVTimeZoneMatchesTimeZone(ics, timeZone, 2024, 2032);
    }

    [Fact]
    public void ToIcs_DoesNotRepeatAYearSpecificRuleInLaterYears()
    {
        var timeZone = CreateTimeZoneWithYearlyRules(
            "Test/Casablanca",
            TimeSpan.Zero,
            firstYear: 2024,
            lastYear: 2024,
            _ => new DateTime(2024, 1, 1),
            _ => new DateTime(2024, 3, 10, 2, 0, 0));

        var ics = WriteVTimeZone(timeZone, new DateTime(2024, 01, 15, 12, 00, 00));

        Assert.DoesNotContain("RRULE:", ics);
        Assert.DoesNotContain("T235959", ics);
        AssertVTimeZoneMatchesTimeZone(ics, timeZone, 2023, 2027);
    }

    [Fact]
    public void ToIcs_WritesAChangeOfTheStandardOffset()
    {
        // Kazakhstan moved from +06:00 to +05:00 on 2024-03-01, without daylight saving time on either side. A period
        // without daylight saving time is a rule with a zero delta, whose transitions are never applied.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(1992, 1, 1),
            new DateTime(2024, 2, 29),
            TimeSpan.Zero,
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), month: 1, day: 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), month: 1, day: 2),
            baseUtcOffsetDelta: TimeSpan.FromHours(1));
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Almaty", TimeSpan.FromHours(5), "Test Almaty", "STD", "DST", [rule]);

        var ics = WriteVTimeZone(timeZone, new DateTime(2024, 01, 15, 12, 00, 00));

        // The runtime ends the rule at midnight in the base offset, which is 01:00 in the +06:00 offset.
        Assert.Equal(1, CountContentLines(ics, "BEGIN:STANDARD"));
        Assert.DoesNotContain("BEGIN:DAYLIGHT", ics);
        Assert.Contains("TZOFFSETFROM:+0600\r\nTZOFFSETTO:+0500\r\n", ics);
        AssertVTimeZoneMatchesTimeZone(ics, timeZone, 2024, 2027);
    }

    [Fact]
    public void ToIcs_WritesTheOffsetInEffectBeforeAChangeOfRule()
    {
        // Scoresbysund was at -01:00 with daylight saving time until 2024-03-31, when it moved to -02:00. The rule of
        // the new offset must not describe January 2024.
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), month: 3, week: 5, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), month: 10, week: 5, DayOfWeek.Sunday);
        var rules = new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2023, 12, 31), TimeSpan.FromHours(1), daylightStart, daylightEnd, baseUtcOffsetDelta: TimeSpan.FromHours(1)),
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2024, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd),
        };
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Scoresbysund", TimeSpan.FromHours(-2), "Test Scoresbysund", "STD", "DST", rules);

        var ics = WriteVTimeZone(timeZone, new DateTime(2023, 12, 15, 12, 00, 00));

        Assert.Contains("TZOFFSETFROM:-0100\r\n", ics);
        AssertVTimeZoneMatchesTimeZone(ics, timeZone, 2023, 2030);
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    [Theory]
    [InlineData("Europe/Paris", 2024)]
    [InlineData("America/New_York", 2024)]
    [InlineData("Asia/Jerusalem", 2026)]
    [InlineData("Africa/Casablanca", 2024)]
    [InlineData("Africa/Cairo", 2024)]
    [InlineData("Asia/Almaty", 2024)]
    [InlineData("America/Scoresbysund", 2024)]
    [InlineData("America/Nuuk", 2026)]
    [InlineData("America/Santiago", 2026)]
    [InlineData("America/Asuncion", 2024)]
    [InlineData("Australia/Sydney", 2010)]
    public void ToIcs_WritesAVTimeZoneMatchingTheOffsetsOfASystemTimeZone(string id, int year)
    {
        // An IANA identifier does not resolve on Windows when globalization is invariant. The time zone database
        // differs across machines, so the VTIMEZONE is compared with the offsets this machine reports.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timeZone))
        {
            global::Xunit.Assert.Skip($"The time zone '{id}' is not available on this machine.");
            return;
        }

        var ics = WriteVTimeZone(timeZone, new DateTime(year, 01, 15, 12, 00, 00));

        AssertVTimeZoneMatchesTimeZone(ics, timeZone, year, year + 4);
    }
#endif
}
