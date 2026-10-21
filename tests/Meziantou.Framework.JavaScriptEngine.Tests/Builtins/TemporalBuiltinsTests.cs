namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class TemporalBuiltinsTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 7, 15, 10, 20, 30, 456, TimeSpan.Zero);

    private static string Evaluate(string code, string timeZone = "America/New_York")
    {
        using var engine = new JsEngine(new JsEngineOptions { TimeZone = timeZone, TimeProvider = new FixedTimeProvider(FixedNow) });
        var realm = engine.CreateRealm();
        return realm.Execute(code).AsString();
    }

    /// <summary>Gets the name of the error thrown by the code, or "none".</summary>
    private static string ErrorName(string code) => Evaluate("try { " + code + "; 'none' } catch (e) { e.constructor.name }");

    [Fact]
    public void Now_UsesTheTimeProviderAndTheTimeZoneOfTheEngine()
    {
        Assert.Equal("2024-07-15T10:20:30.456Z", Evaluate("Temporal.Now.instant().toString()"));
        Assert.Equal("America/New_York", Evaluate("Temporal.Now.timeZoneId()"));
        Assert.Equal("2024-07-15T06:20:30.456", Evaluate("Temporal.Now.plainDateTimeISO().toString()"));
        Assert.Equal("2024-07-15", Evaluate("Temporal.Now.plainDateISO().toString()"));
        Assert.Equal("06:20:30.456", Evaluate("Temporal.Now.plainTimeISO().toString()"));
        Assert.Equal("2024-07-15T06:20:30.456-04:00[America/New_York]", Evaluate("Temporal.Now.zonedDateTimeISO().toString()"));
        Assert.Equal("2024-07-15T19:50:30.456+09:30[Australia/Darwin]", Evaluate("Temporal.Now.zonedDateTimeISO('Australia/Darwin').toString()"));
    }

    [Fact]
    public void Now_TimeZoneIdIsThePrimaryIdentifier()
    {
        Assert.Equal("Asia/Kolkata", Evaluate("Temporal.Now.timeZoneId()", timeZone: "asia/calcutta"));
    }

    [Fact]
    public void TimeZone_LinksKeepTheirIdentifierAndAreEqualToTheirZone()
    {
        Assert.Equal("Asia/Calcutta", Evaluate("new Temporal.ZonedDateTime(0n, 'ASIA/CALCUTTA').timeZoneId"));
        Assert.Equal("true", Evaluate("String(new Temporal.ZonedDateTime(0n, 'Asia/Calcutta').equals(new Temporal.ZonedDateTime(0n, 'Asia/Kolkata')))"));
        Assert.Equal("+05:30", Evaluate("new Temporal.ZonedDateTime(0n, '+0530').timeZoneId"));
        Assert.Equal("RangeError", ErrorName("new Temporal.ZonedDateTime(0n, 'Mars/Olympus_Mons')"));
    }

    [Fact]
    public void ZonedDateTime_DaylightSavingTimeGapAndOverlap()
    {
        // 2024-03-10T02:30 does not exist in New York, 2024-11-03T01:30 exists twice
        Assert.Equal("2024-03-10T03:30:00-04:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-03-10T02:30[America/New_York]').toString()"));
        Assert.Equal("2024-03-10T01:30:00-05:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-03-10T02:30[America/New_York]', { disambiguation: 'earlier' }).toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.ZonedDateTime.from('2024-03-10T02:30[America/New_York]', { disambiguation: 'reject' })"));
        Assert.Equal("2024-11-03T01:30:00-04:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-11-03T01:30[America/New_York]').toString()"));
        Assert.Equal("2024-11-03T01:30:00-05:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-11-03T01:30[America/New_York]', { disambiguation: 'later' }).toString()"));
        Assert.Equal("2024-11-03T01:30:00-05:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-11-03T01:30-05:00[America/New_York]').toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.ZonedDateTime.from('2024-11-03T01:30-06:00[America/New_York]')"));
        Assert.Equal("23,25,24", Evaluate("['2024-03-10', '2024-11-03', '2024-07-15'].map(d => Temporal.PlainDate.from(d).toZonedDateTime('America/New_York').hoursInDay).join()"));
    }

    [Fact]
    public void ZonedDateTime_Transitions()
    {
        Assert.Equal("2024-03-10T03:00:00-04:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-01-01T00:00[America/New_York]').getTimeZoneTransition('next').toString()"));
        Assert.Equal("2023-11-05T01:00:00-05:00[America/New_York]", Evaluate("Temporal.ZonedDateTime.from('2024-01-01T00:00[America/New_York]').getTimeZoneTransition({ direction: 'previous' }).toString()"));
        Assert.Equal("null", Evaluate("String(Temporal.ZonedDateTime.from('2024-01-01T00:00[UTC]').getTimeZoneTransition('next'))"));
        Assert.Equal("null", Evaluate("String(Temporal.ZonedDateTime.from('2024-01-01T00:00+01:00[+01:00]').getTimeZoneTransition('next'))"));

        // Midnight is skipped on 2016-10-16 in Sao Paulo: the day starts at 01:00
        Assert.Equal("2016-10-16T01:00:00-02:00[America/Sao_Paulo]", Evaluate("Temporal.PlainDate.from('2016-10-16').toZonedDateTime('America/Sao_Paulo').toString()"));
    }

    [Fact]
    public void Parsing_RejectsHour24AndUnknownCriticalAnnotations()
    {
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDateTime.from('2024-01-01T24:00')"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainTime.from('24:00')"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from('2024-01-01[!foo=bar]')"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from('2024-01-01T00:00Z')"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from('2023-02-29')"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from('2024-01-01[u-ca=unknown]')"));
        Assert.Equal("2024-01-01", Evaluate("Temporal.PlainDate.from('2024-01-01[foo=bar][u-ca=iso8601]').toString()"));
        Assert.Equal("23:59:59", Evaluate("Temporal.PlainTime.from('23:59:60').toString()"));
        Assert.Equal("12:34:56.123456789", Evaluate("Temporal.PlainTime.from('T123456,123456789').toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainTime.from('1214')"));
        Assert.Equal("02:30:00", Evaluate("Temporal.PlainTime.from('0230').toString()"));
        Assert.Equal("1970-01-01T00:00:00.000000001Z", Evaluate("Temporal.Instant.from('1970-01-01T00:00:00.000000001+00:00:00.000000000').toString()"));
        Assert.Equal("-271821-04-20", Evaluate("Temporal.PlainDate.from('-271821-04-20').toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from('-000000-01-01')"));
    }

    [Fact]
    public void Duration_ExactArithmetic()
    {
        Assert.Equal("P1DT23H7M11.50040904S", Evaluate("Temporal.Duration.from('PT46H66M71.50040904S').round({ largestUnit: 'day' }).toString()"));
        Assert.Equal("PT1.5H", Evaluate("'PT' + Temporal.Duration.from('PT1H30M').total('hour') + 'H'"));
        Assert.Equal("PT1H0.000000001S", Evaluate("Temporal.Duration.from('PT1H').add({ nanoseconds: 1 }).toString()"));
        Assert.Equal("9007199254740992,1.000000001", Evaluate("[Temporal.Duration.from({ seconds: Number.MAX_SAFE_INTEGER, nanoseconds: 999999999 }).total('second'), Temporal.Duration.from({ seconds: 1, nanoseconds: 1 }).total('second')].join()"));
        Assert.Equal("RangeError", ErrorName("new Temporal.Duration(0, 0, 0, 0, 0, 0, Number.MAX_SAFE_INTEGER + 1)"));
        Assert.Equal("RangeError", ErrorName("new Temporal.Duration(1, -1)"));
        Assert.Equal("P1Y28D", Evaluate("Temporal.Duration.from({ months: 13 }).round({ largestUnit: 'years', relativeTo: '2024-01-31' }).toString()"));
        Assert.Equal("P1M", Evaluate("Temporal.PlainDate.from('2012-01-01').until('2012-02-01', { largestUnit: 'months', smallestUnit: 'weeks', roundingMode: 'ceil' }).toString()"));
        Assert.Equal("P1M1D", Evaluate("Temporal.PlainDate.from('2024-01-31').until('2024-03-01', { largestUnit: 'month' }).toString()"));
        Assert.Equal("P10000Y", Evaluate("Temporal.PlainDate.from('-005000-01-01').until('+005000-01-01', { largestUnit: 'year' }).toString()"));
    }

    [Fact]
    public void Date_ToTemporalInstant()
    {
        Assert.Equal("2024-07-15T10:20:30.456Z", Evaluate("new Date(Date.UTC(2024, 6, 15, 10, 20, 30, 456)).toTemporalInstant().toString()"));
        Assert.Equal("RangeError", ErrorName("new Date(NaN).toTemporalInstant()"));
        Assert.Equal("TypeError", ErrorName("Date.prototype.toTemporalInstant.call({})"));
    }

    [Fact]
    public void Instant_EpochNanosecondsAreBigInts()
    {
        Assert.Equal("8640000000000000000000", Evaluate("Temporal.Instant.fromEpochNanoseconds(8640000000000000000000n).epochNanoseconds.toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.Instant.fromEpochNanoseconds(8640000000000000000001n)"));
        Assert.Equal("-1", Evaluate("String(new Temporal.Instant(-1n).epochMilliseconds)"));
        Assert.Equal("+275760-09-13T00:00:00Z", Evaluate("Temporal.Instant.fromEpochMilliseconds(8.64e15).toString()"));
    }

    [Fact]
    public void ToStringTags()
    {
        Assert.Equal("[object Temporal],[object Temporal.Now],[object Temporal.Duration],[object Temporal.PlainDate]", Evaluate("[Temporal, Temporal.Now, new Temporal.Duration(), new Temporal.PlainDate(2024, 1, 1)].map(o => Object.prototype.toString.call(o)).join()"));
    }

    [Fact]
    public void Calendars_IdentifiersAreCanonicalized()
    {
        Assert.Equal("gregory,japanese,ethioaa", Evaluate("['GREGORY', 'Japanese', 'ethiopic-amete-alem'].map(c => new Temporal.PlainDate(2024, 1, 1, c).calendarId).join()"));
        Assert.Equal("RangeError", ErrorName("new Temporal.PlainDate(2024, 1, 1, 'islamic')"));
        Assert.Equal("2024-01-01[u-ca=gregory]", Evaluate("Temporal.PlainDate.from('2024-01-01[u-ca=gregory]').toString()"));
    }

    [Theory]
    [InlineData("gregory", "2024-03-15", "ce 2024 2024 M03 15")]
    [InlineData("gregory", "-000043-03-15", "bce 44 -43 M03 15")]
    [InlineData("buddhist", "2024-03-15", "be 2567 2567 M03 15")]
    [InlineData("roc", "2024-03-15", "roc 113 113 M03 15")]
    [InlineData("roc", "1911-03-15", "broc 1 0 M03 15")]
    [InlineData("japanese", "2019-04-30", "heisei 31 2019 M04 30")]
    [InlineData("japanese", "2019-05-01", "reiwa 1 2019 M05 1")]
    [InlineData("japanese", "1872-12-31", "ce 1872 1872 M12 31")]
    [InlineData("japanese", "1873-01-01", "meiji 6 1873 M01 1")]
    [InlineData("coptic", "2024-03-15", "am 1740 1740 M07 6")]
    [InlineData("ethiopic", "2024-03-15", "am 2016 2016 M07 6")]
    [InlineData("ethioaa", "2024-03-15", "aa 7516 7516 M07 6")]
    [InlineData("indian", "2024-03-15", "shaka 1945 1945 M12 25")]
    [InlineData("persian", "2024-03-15", "ap 1402 1402 M12 25")]
    public void Calendars_IsoToCalendarDate(string calendar, string isoDate, string expected)
    {
        Assert.Equal(expected, Evaluate($"const d = Temporal.PlainDate.from('{isoDate}').withCalendar('{calendar}'); [d.era, d.eraYear, d.year, d.monthCode, d.day].join(' ')"));
    }

    [Fact]
    public void Calendars_EraFieldsAndArithmetic()
    {
        Assert.Equal("2018-04-25[u-ca=japanese]", Evaluate("Temporal.PlainDate.from({ calendar: 'japanese', era: 'reiwa', eraYear: 0, monthCode: 'M04', day: 25 }).toString()"));
        Assert.Equal("RangeError", ErrorName("Temporal.PlainDate.from({ calendar: 'gregory', era: 'xx', eraYear: 1, month: 1, day: 1 })"));
        Assert.Equal("TypeError", ErrorName("Temporal.PlainDate.from({ calendar: 'gregory', era: 'ce', month: 1, day: 1 })"));
        Assert.Equal("P1Y1M1D", Evaluate("Temporal.PlainDate.from({ calendar: 'gregory', year: 2024, month: 1, day: 31 }).until(Temporal.PlainDate.from({ calendar: 'gregory', year: 2025, month: 3, day: 1 }), { largestUnit: 'years' }).toString()"));
        Assert.Equal("2024-02-29[u-ca=buddhist]", Evaluate("Temporal.PlainDate.from({ calendar: 'buddhist', year: 2567, month: 1, day: 31 }).add({ months: 1 }).toString()"));
        Assert.Equal("1972-02-29[u-ca=gregory]", Evaluate("Temporal.PlainMonthDay.from({ calendar: 'gregory', monthCode: 'M02', day: 30 }).toString()"));
    }

    [Theory]
    [InlineData("buddhist")]
    [InlineData("coptic")]
    [InlineData("ethioaa")]
    [InlineData("ethiopic")]
    [InlineData("gregory")]
    [InlineData("indian")]
    [InlineData("japanese")]
    [InlineData("persian")]
    [InlineData("roc")]
    public void Calendars_RoundTripAndArithmetic(string calendar)
    {
        // Every day of a few years converts to the calendar and back, and adding the months one by one is the same as
        // adding them at once
        var code = $$"""
            let errors = [];
            let date = Temporal.PlainDate.from('2019-01-01').withCalendar('{{calendar}}');
            const start = date;
            for (let i = 0; i < 2000; i++) {
                const copy = Temporal.PlainDate.from({ calendar: '{{calendar}}', year: date.year, monthCode: date.monthCode, day: date.day });
                if (!copy.equals(date)) errors.push(date.toString());
                const next = date.add({ days: 1 });
                if (next.day !== 1 && (next.day !== date.day + 1 || next.month !== date.month)) errors.push('next ' + date.toString());
                if (next.day === 1 && date.day !== date.daysInMonth) errors.push('end ' + date.toString());
                date = next;
            }

            const months = start.until(date, { largestUnit: 'months' });
            if (!start.add(months).equals(date)) errors.push('months ' + months);
            const years = start.until(date, { largestUnit: 'years' });
            if (!start.add(years).equals(date)) errors.push('years ' + years);
            const backwards = date.until(start, { largestUnit: 'years' });
            if (!date.add(backwards).equals(start)) errors.push('backwards ' + backwards);
            errors.join()
            """;
        Assert.Equal("", Evaluate(code));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
