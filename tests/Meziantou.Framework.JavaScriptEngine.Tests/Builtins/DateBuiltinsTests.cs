namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class DateBuiltinsTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 7, 15, 10, 20, 30, 456, TimeSpan.Zero);

    private static FundamentalsTestContext CreateContext(string timeZone = "Europe/Paris")
    {
        return new FundamentalsTestContext(new JsEngineOptions { TimeZone = timeZone, TimeProvider = new FixedTimeProvider(FixedNow, TimeZoneInfo.Utc) });
    }

    private static double Ms(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, second, millisecond, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    private static JsValue NewDate(FundamentalsTestContext context, params JsValue[] arguments) => JsOperations.Construct(context.Global("Date"), arguments);

    private static JsValue Invoke(JsValue date, string name, params JsValue[] arguments) => FundamentalsTestContext.Invoke(date, name, arguments);

    private static JsValue Invoke(JsValue date, JsSymbol name, params JsValue[] arguments) => JsOperations.Invoke(date, name, arguments);

    private static double GetTime(JsValue date) => Number(Invoke(date, "getTime"));

    private static double Number(JsValue value) => value.AsNumber();

    private static string Text(JsValue value) => value.AsJsString().ToString();

    private static double Parse(FundamentalsTestContext context, string value) => Number(context.CallStatic("Date", "parse", value));

    [Fact]
    public void ConstructorAndPrototypeProperties()
    {
        using var context = CreateContext();
        var date = context.Global("Date");
        Assert.Same(context.Intrinsics.Date, date);
        Assert.Equal(7, Number(FundamentalsTestContext.Get(date, "length")));
        Assert.Equal("Date", Text(FundamentalsTestContext.Get(date, "name")));
        Assert.Equal(7, Number(FundamentalsTestContext.Get(FundamentalsTestContext.Get(date, "UTC"), "length")));
        Assert.Equal(1, Number(FundamentalsTestContext.Get(FundamentalsTestContext.Get(date, "parse"), "length")));

        var prototype = FundamentalsTestContext.Get(date, "prototype").AsObject();
        Assert.Same(context.Intrinsics.DatePrototype, prototype);
        Assert.Equal(ObjectClass.Object, prototype.Class);
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Date", "getTime", prototype));

        Assert.Equal(4, Number(FundamentalsTestContext.Get(FundamentalsTestContext.Get(prototype, "setHours"), "length")));
        Assert.Equal(3, Number(FundamentalsTestContext.Get(FundamentalsTestContext.Get(prototype, "setFullYear"), "length")));
        Assert.Same(FundamentalsTestContext.Get(prototype, "toUTCString").AsObject(), FundamentalsTestContext.Get(prototype, "toGMTString").AsObject());

        Assert.True(prototype.GetOwnProperty(WellKnownSymbols.ToPrimitive, out var toPrimitive));
        Assert.Equal(PropertyFlags.Configurable, toPrimitive.Flags);
        Assert.Equal("[Symbol.toPrimitive]", Text(FundamentalsTestContext.Get(toPrimitive.Value, "name")));
        Assert.Equal(1, Number(FundamentalsTestContext.Get(toPrimitive.Value, "length")));
    }

    [Fact]
    public void Now_UsesTheTimeProvider()
    {
        using var context = CreateContext();
        Assert.Equal(FixedNow.ToUnixTimeMilliseconds(), Number(context.CallStatic("Date", "now")));
        Assert.Equal(FixedNow.ToUnixTimeMilliseconds(), Number(Invoke(NewDate(context), "getTime")));

        // Date() called as a function returns the current time as a string
        var text = Text(context.Global("Date").Call(JsValue.Undefined, [1, 2]));
        Assert.Equal("Mon Jul 15 2024 12:20:30 GMT+0200 (CEST)", text);
    }

    [Fact]
    public void Constructor_Components_LocalTime()
    {
        using var context = CreateContext();
        var date = NewDate(context, 2022, 1, 1);
        Assert.Equal(Ms(2022, 1, 31, 23), Number(Invoke(date, "valueOf")));
        Assert.Equal("Tue Feb 01 2022 00:00:00 GMT+0100 (CET)", Text(Invoke(date, "toString")));
        Assert.Equal("Tue Feb 01 2022", Text(Invoke(date, "toDateString")));
        Assert.Equal("00:00:00 GMT+0100 (CET)", Text(Invoke(date, "toTimeString")));
        Assert.Equal("Mon, 31 Jan 2022 23:00:00 GMT", Text(Invoke(date, "toUTCString")));
        Assert.Equal("2022-01-31T23:00:00.000Z", Text(Invoke(date, "toISOString")));
        Assert.Equal("Tue Feb 01 2022 00:00:00 GMT+0100 (CET)", Text(Invoke(date, "toLocaleString")));

        // Two-digit years are 19xx, and the components overflow
        Assert.Equal(Ms(1998, 12, 31, 23), GetTime(NewDate(context, 99, 0)));
        Assert.Equal(Ms(1999, 12, 31, 23), GetTime(NewDate(context, 99, 11, 31, 23, 59, 59, 1000)));
        Assert.True(double.IsNaN(Number(Invoke(NewDate(context, double.MaxValue, double.MaxValue), "getTime"))));
    }

    [Fact]
    public void Constructor_OneArgument()
    {
        using var context = CreateContext();
        var date = NewDate(context, 1438560000000);
        Assert.Equal(1438560000000, Number(Invoke(date, "getTime")));

        // A Date argument gives its time value without calling valueOf or toString
        date.AsObject().CreateDataPropertyOrThrow("valueOf", context.Function((_, _) => throw new InvalidOperationException("valueOf must not be called")));
        Assert.Equal(1438560000000, Number(Invoke(NewDate(context, date), "getTime")));

        Assert.Equal(1465152000000, Number(Invoke(NewDate(context, "2016-06-05T18:40:00.000Z"), "getTime")));
        Assert.Equal(-0.0, Number(Invoke(NewDate(context, -0.0), "getTime")));
        Assert.True(double.IsPositive(Number(Invoke(NewDate(context, -0.0), "getTime"))));
        Assert.True(double.IsNaN(Number(Invoke(NewDate(context, 8.64e15 + 1), "getTime"))));
        Assert.Equal(8.64e15, Number(Invoke(NewDate(context, 8.64e15), "getTime")));
        Assert.Equal(1, Number(Invoke(NewDate(context, true), "getTime")));
        Assert.Equal("Invalid Date", Text(Invoke(NewDate(context, double.NaN), "toString")));
        context.AssertThrows(ErrorType.RangeError, () => Invoke(NewDate(context, double.NaN), "toISOString"));
    }

    [Fact]
    public void Constructor_CoercionOrder()
    {
        using var context = CreateContext();
        var log = new List<string>();
        JsValue Logged(string name, JsValue value)
        {
            var obj = context.NewObject();
            obj.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) =>
            {
                log.Add(name);
                return value;
            }));
            return obj;
        }

        NewDate(context, Logged("year", 0), Logged("month", 0), Logged("date", 1), Logged("hours", 0), Logged("minutes", 0), Logged("seconds", 0), Logged("ms", 0));
        Assert.Equal(["year", "month", "date", "hours", "minutes", "seconds", "ms"], log);
    }

    [Fact]
    public void Utc()
    {
        using var context = CreateContext();
        Assert.Equal(29312, Number(context.CallStatic("Date", "UTC", 1970, 0, 1, 80063993375, 29, 1, -288230376151711740.0)));
        Assert.Equal(34447360, Number(context.CallStatic("Date", "UTC", 1970, 0, 213503982336, 0, 0, 0, -18446744073709552000.0)));
        Assert.Equal(-62198755200000, Number(context.CallStatic("Date", "UTC", -1, 0)));
        Assert.Equal(-2208988800000, Number(context.CallStatic("Date", "UTC", -0.999999, 0)));
        Assert.Equal(915148800000, Number(context.CallStatic("Date", "UTC", 99.999999, 0)));
        Assert.Equal(-59011459200000, Number(context.CallStatic("Date", "UTC", 100, 0)));
        Assert.Equal(1485907200000, Number(context.CallStatic("Date", "UTC", 2016, 13)));
        Assert.Equal(1454371200000, Number(context.CallStatic("Date", "UTC", 2016, 2, -27)));
        Assert.Equal(1467676799999, Number(context.CallStatic("Date", "UTC", 2016, 6, 5, 0, 0, 0, -1)));
        Assert.Equal(-124334438400000, Number(context.CallStatic("Date", "UTC", -1970.9, -0.9, -0.9, -0.9, -0.9, -0.9, -0.9)));
        Assert.Equal(8.64e15, Number(context.CallStatic("Date", "UTC", 275760, 8, 13, 0, 0, 0, 0)));
        Assert.True(double.IsNaN(Number(context.CallStatic("Date", "UTC", 275760, 8, 13, 0, 0, 0, 1))));
        Assert.True(double.IsNaN(Number(context.CallStatic("Date", "UTC"))));
        Assert.Equal(Ms(2017, 1, 1), Number(context.CallStatic("Date", "UTC", 2017)));
        Assert.True(double.IsNaN(Number(context.CallStatic("Date", "UTC", double.MaxValue, double.MaxValue))));
        Assert.True(double.IsNaN(Number(context.CallStatic("Date", "UTC", double.PositiveInfinity, 0))));
    }

    [Fact]
    public void LocalGetters()
    {
        using var context = CreateContext("America/New_York");
        var date = NewDate(context, Ms(2024, 7, 4, 3, 5, 6, 7));
        Assert.Equal(2024, Number(Invoke(date, "getFullYear")));
        Assert.Equal(6, Number(Invoke(date, "getMonth")));
        Assert.Equal(3, Number(Invoke(date, "getDate")));
        Assert.Equal(3, Number(Invoke(date, "getDay")));
        Assert.Equal(23, Number(Invoke(date, "getHours")));
        Assert.Equal(5, Number(Invoke(date, "getMinutes")));
        Assert.Equal(6, Number(Invoke(date, "getSeconds")));
        Assert.Equal(7, Number(Invoke(date, "getMilliseconds")));
        Assert.Equal(240, Number(Invoke(date, "getTimezoneOffset")));
        Assert.Equal(124, Number(Invoke(date, "getYear")));
        Assert.Equal(4, Number(Invoke(date, "getUTCDate")));
        Assert.Equal(3, Number(Invoke(date, "getUTCHours")));
        Assert.Equal(300, Number(Invoke(NewDate(context, Ms(2024, 1, 4)), "getTimezoneOffset")));
        Assert.True(double.IsNaN(Number(Invoke(NewDate(context, double.NaN), "getHours"))));
        Assert.Equal("Wed Jul 03 2024 23:05:06 GMT-0400 (EDT)", Text(Invoke(date, "toString")));
    }

    [Fact]
    public void SubMinuteOffsets()
    {
        using var context = CreateContext("Europe/Amsterdam");
        var date = NewDate(context, Ms(1900, 1, 1));
        Assert.Equal(-19.533333333333335, Number(Invoke(date, "getTimezoneOffset")));
        Assert.Equal("Mon Jan 01 1900 00:19:32 GMT+0019 (AMT)", Text(Invoke(date, "toString")));
    }

    [Fact]
    public void LocalTime_GapsAndOverlaps()
    {
        using var context = CreateContext("America/New_York");

        // 02:30 does not exist on 2024-03-10: the offset before the transition applies (03:30 EDT)
        var skipped = NewDate(context, 2024, 2, 10, 2, 30);
        Assert.Equal(Ms(2024, 3, 10, 7, 30), Number(Invoke(skipped, "getTime")));
        Assert.Equal(3, Number(Invoke(skipped, "getHours")));

        // 01:30 happens twice on 2024-11-03: the earlier one (EDT)
        var repeated = NewDate(context, 2024, 10, 3, 1, 30);
        Assert.Equal(Ms(2024, 11, 3, 5, 30), Number(Invoke(repeated, "getTime")));
    }

    [Fact]
    public void ToIsoString_ExtendedYears()
    {
        using var context = CreateContext();
        Assert.Equal("-271821-04-20T00:00:00.000Z", Text(Invoke(NewDate(context, -8.64e15), "toISOString")));
        Assert.Equal("+275760-09-13T00:00:00.000Z", Text(Invoke(NewDate(context, 8.64e15), "toISOString")));
        Assert.Equal("0000-01-01T00:00:00.000Z", Text(Invoke(NewDate(context, -62167219200000), "toISOString")));
        Assert.Equal("-000001-12-31T23:59:59.999Z", Text(Invoke(NewDate(context, -62167219200001), "toISOString")));
        Assert.Equal("+010000-01-01T00:00:00.000Z", Text(Invoke(NewDate(context, 253402300800000), "toISOString")));
        Assert.Equal("Thu Jul 01 -0001", Text(Invoke(NewDate(context, "-000001-07-01T00:00Z"), "toDateString")));
        Assert.Equal("Wed Jul 01 -123456", Text(Invoke(NewDate(context, "-123456-07-01T00:00Z"), "toDateString")));
        Assert.Equal("Wed, 01 Jan 0020 00:00:00 GMT", Text(Invoke(NewDate(context, "0020-01-01T00:00:00Z"), "toUTCString")));

        // A year written with more than two digits is not a two-digit year
        Assert.Equal(Ms(1, 2, 3, 4, 5, 6), Parse(context, Text(Invoke(NewDate(context, Ms(1, 2, 3, 4, 5, 6)), "toUTCString"))));
    }

    [Theory]
    [InlineData("1970-01-01", 0)]
    [InlineData("1970", 0)]
    [InlineData("1970-02", 2678400000)]
    [InlineData("1970-01-01T00:00:00Z", 0)]
    [InlineData("1970-01-01T00:00:00.5Z", 500)]
    [InlineData("1970-01-01T00:00:00.123456Z", 123)]
    [InlineData("1970-01-01T01:00+01:00", 0)]
    [InlineData("1970-01-01T00:00:00.000-02:30", 9000000)]
    [InlineData("1970-01-01T24:00:00Z", 86400000)]
    [InlineData("1970-01-01T24:00Z", 86400000)]
    [InlineData("-271821-04-20T00:00:00.000Z", -8.64e15)]
    [InlineData("+275760-09-13T00:00:00.000Z", 8.64e15)]
    [InlineData("+002016-06-05T18:40:00.000Z", 1465152000000)]
    [InlineData("Thu, 01 Jan 1970 00:00:00 GMT", 0)]
    [InlineData("Thu Jan 01 1970 01:00:00 GMT+0100 (CET)", 0)]
    [InlineData("Thu Jan 01 1970 01:00:00 GMT+0100 (Central European (Standard) Time)", 0)]
    [InlineData("Jan 1 1970 00:00:00 UTC", 0)]
    [InlineData("1 January 1970 00:00 GMT", 0)]
    [InlineData("1970-01-01 00:00:00Z", 0)]
    [InlineData("Thu Jan 01 1970 00:00:00 GMT-0500 (EST)", 18000000)]
    [InlineData("Jan 1 1970 00:00:00 EST", 18000000)]
    [InlineData("Jan 1 1970 12:00:00 am GMT", 0)]
    [InlineData("Jan 1 1970 12:30 pm GMT", 45000000)]
    [InlineData("1970-01-01 01:00:00 +01:00", 0)]
    public void Parse_Utc(string value, double expected)
    {
        using var context = CreateContext();
        Assert.Equal(expected, Parse(context, value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("-000000-03-31T00:45Z")]
    [InlineData("-000000-03-31T01:45")]
    [InlineData("-000000-03-31T01:45:00+01:00")]
    [InlineData("-271821-04-19T23:59:59.999Z")]
    [InlineData("+275760-09-13T00:00:00.001Z")]
    [InlineData("1970-13-01")]
    [InlineData("1970-01-01T24:00:01Z")]
    [InlineData("1970-01-01T25:00Z")]
    [InlineData("1970-01-01T00:60Z")]
    [InlineData("1997-03-08T11:19:10-07")]
    [InlineData("1997-03-08T")]
    [InlineData("1997-3-8T11:19:20")]
    [InlineData("1997-03-08T1:19")]
    [InlineData("1997-03-08 11")]
    [InlineData("99/1/99")]
    [InlineData("13/13/13")]
    [InlineData("0/10/0")]
    [InlineData("may 1999 1999")]
    [InlineData("may 0 0")]
    [InlineData("Jan 1 1970 25:00")]
    public void Parse_Invalid(string value)
    {
        using var context = CreateContext();
        Assert.True(double.IsNaN(Parse(context, value)), value);
    }

    [Theory]
    [InlineData("1970-01-01T00:00:00", 1970, 1, 1, 0, 0, 0, 0)]
    [InlineData("1970-01-01T00:00", 1970, 1, 1, 0, 0, 0, 0)]
    [InlineData("1997-03-08T11:19:20", 1997, 3, 8, 11, 19, 20, 0)]
    [InlineData("1997-03-08 11:19:20", 1997, 3, 8, 11, 19, 20, 0)]
    [InlineData("1997-3-8 11:19:20", 1997, 3, 8, 11, 19, 20, 0)]
    [InlineData("+001997-3-8 11:19:20", 1997, 3, 8, 11, 19, 20, 0)]
    [InlineData("1997-03-08 1:1:1.01", 1997, 3, 8, 1, 1, 1, 10)]
    [InlineData("1997-03-08T01:01:01.01", 1997, 3, 8, 1, 1, 1, 10)]
    [InlineData("10/31/2010 08:00", 2010, 10, 31, 8, 0, 0, 0)]
    [InlineData("10/31/10", 2010, 10, 31, 0, 0, 0, 0)]
    [InlineData("10/31/50", 1950, 10, 31, 0, 0, 0, 0)]
    [InlineData("2010/10/31", 2010, 10, 31, 0, 0, 0, 0)]
    [InlineData("may 1 1999", 1999, 5, 1, 0, 0, 0, 0)]
    [InlineData("1 may 1999", 1999, 5, 1, 0, 0, 0, 0)]
    [InlineData("1 1999 may", 1999, 5, 1, 0, 0, 0, 0)]
    [InlineData("may 1999 1", 1999, 5, 1, 0, 0, 0, 0)]
    [InlineData("1999 may 1", 1999, 5, 1, 0, 0, 0, 0)]
    [InlineData("may 1 5", 2005, 5, 1, 0, 0, 0, 0)]
    [InlineData("Tuesday, February 1, 2022 3:04:05 PM", 2022, 2, 1, 15, 4, 5, 0)]
    [InlineData("Feb 1 2022", 2022, 2, 1, 0, 0, 0, 0)]
    [InlineData("1997-03-08 24:00", 1997, 3, 9, 0, 0, 0, 0)]
    public void Parse_Local(string value, int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
        using var context = CreateContext("America/New_York");
        Assert.Equal(GetTime(NewDate(context, year, month - 1, day, hour, minute, second, millisecond)), Parse(context, value));
    }

    [Theory]
    [InlineData("Europe/Paris")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Pacific/Chatham")]
    [InlineData("UTC")]
    public void Parse_RoundTrip(string timeZone)
    {
        using var context = CreateContext(timeZone);
        foreach (var value in (double[])[0, 1465152000000, Ms(1950, 1, 1, 12, 34, 56), Ms(2024, 7, 1, 23, 59, 59), Ms(1960, 2, 3, 4, 5, 6)])
        {
            var date = NewDate(context, value);
            Assert.Equal(value, Parse(context, Text(Invoke(date, "toString"))));
            Assert.Equal(value, Parse(context, Text(Invoke(date, "toUTCString"))));
            Assert.Equal(value, Parse(context, Text(Invoke(date, "toISOString"))));
        }
    }

    [Fact]
    public void Setters_Local()
    {
        using var context = CreateContext("America/New_York");
        var date = NewDate(context, 2024, 0, 15, 10, 20, 30, 400);
        Assert.Equal(GetTime(NewDate(context, 2024, 0, 15, 11, 20, 30, 400)), Number(Invoke(date, "setHours", 11)));
        Assert.Equal(11, Number(Invoke(date, "getHours")));
        Invoke(date, "setMinutes", 1, 2, 3);
        Assert.Equal(1, Number(Invoke(date, "getMinutes")));
        Assert.Equal(2, Number(Invoke(date, "getSeconds")));
        Assert.Equal(3, Number(Invoke(date, "getMilliseconds")));
        Invoke(date, "setMonth", 6);

        // Crossing the transition keeps the local time
        Assert.Equal(11, Number(Invoke(date, "getHours")));
        Assert.Equal(240, Number(Invoke(date, "getTimezoneOffset")));
        Invoke(date, "setDate", 32);
        Assert.Equal(7, Number(Invoke(date, "getMonth")));
        Assert.Equal(1, Number(Invoke(date, "getDate")));
        Invoke(date, "setFullYear", 2000, 1, 29);
        Assert.Equal("Tue Feb 29 2000 11:01:02 GMT-0500 (EST)", Text(Invoke(date, "toString")));
        Invoke(date, "setSeconds", 59, 999);
        Assert.Equal(999, Number(Invoke(date, "getMilliseconds")));
        Invoke(date, "setMilliseconds", 1000);
        Assert.Equal(0, Number(Invoke(date, "getMilliseconds")));
        Assert.Equal(11, Number(Invoke(date, "getHours")));
        Assert.Equal(2, Number(Invoke(date, "getMinutes")));

        // An invalid date stays invalid, except for setFullYear which starts from +0
        var invalid = NewDate(context, double.NaN);
        Assert.True(double.IsNaN(Number(Invoke(invalid, "setHours", 1))));
        Assert.True(double.IsNaN(Number(Invoke(invalid, "getTime"))));
        Invoke(invalid, "setFullYear", 2020);
        Assert.Equal("Wed Jan 01 2020 00:00:00 GMT-0500 (EST)", Text(Invoke(invalid, "toString")));

        // Undefined is a present argument (NaN), unlike a missing one
        Assert.True(double.IsNaN(Number(Invoke(date, "setHours", 1, JsValue.Undefined))));
    }

    [Fact]
    public void Setters_Utc()
    {
        using var context = CreateContext("America/New_York");
        var date = NewDate(context, 0);
        Assert.Equal(Ms(1970, 1, 1, 5, 6, 7, 8), Number(Invoke(date, "setUTCHours", 5, 6, 7, 8)));
        Assert.Equal(Ms(1970, 1, 1, 5, 6, 7, 9), Number(Invoke(date, "setUTCMilliseconds", 9)));
        Assert.Equal(Ms(1970, 1, 1, 5, 1, 7, 9), Number(Invoke(date, "setUTCMinutes", 1)));
        Assert.Equal(Ms(1970, 1, 1, 5, 1, 2, 9), Number(Invoke(date, "setUTCSeconds", 2)));
        Assert.Equal(Ms(1970, 3, 1, 5, 1, 2, 9), Number(Invoke(date, "setUTCMonth", 2)));
        Assert.Equal(Ms(1970, 3, 5, 5, 1, 2, 9), Number(Invoke(date, "setUTCDate", 5)));
        Assert.Equal(Ms(2001, 3, 5, 5, 1, 2, 9), Number(Invoke(date, "setUTCFullYear", 2001)));
        Assert.Equal(123, Number(Invoke(date, "setTime", 123.9)));
        Assert.True(double.IsNaN(Number(Invoke(date, "setTime"))));
        Assert.Equal(Ms(2001, 1, 1), Number(Invoke(date, "setUTCFullYear", 2001)));
        Assert.True(double.IsNaN(Number(Invoke(date, "setUTCFullYear", double.MaxValue, double.MaxValue))));
    }

    [Fact]
    public void Setters_ReadTheDateValueBeforeTheConversions()
    {
        using var context = CreateContext();
        var date = NewDate(context, double.NaN);
        var value = context.NewObject();
        var calls = 0;
        value.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) =>
        {
            calls++;
            Invoke(date, "setTime", 0);
            return 1;
        }));

        Assert.True(double.IsNaN(Number(Invoke(date, "setHours", value))));
        Assert.Equal(1, calls);
        Assert.Equal(0, Number(Invoke(date, "getTime")));

        // All the arguments are converted even when the date is invalid
        var invalid = NewDate(context, double.NaN);
        var log = new List<string>();
        JsValue Logged(string name)
        {
            var obj = context.NewObject();
            obj.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) =>
            {
                log.Add(name);
                return 0;
            }));
            return obj;
        }

        Invoke(invalid, "setHours", Logged("h"), Logged("m"), Logged("s"), Logged("ms"));
        Assert.Equal(["h", "m", "s", "ms"], log);
    }

    [Fact]
    public void AnnexB_SetYear()
    {
        using var context = CreateContext();
        var date = NewDate(context, context.NewObject());
        Assert.True(double.IsNaN(Number(Invoke(date, "getYear"))));
        var expected = Number(Invoke(NewDate(context, 1971, 0), "valueOf"));
        Assert.Equal(expected, Number(Invoke(date, "setYear", 71)));
        Assert.Equal(71, Number(Invoke(date, "getYear")));
        Invoke(date, "setYear", 2005);
        Assert.Equal(2005, Number(Invoke(date, "getFullYear")));
        Assert.True(double.IsNaN(Number(Invoke(date, "setYear", double.NaN))));
        Assert.True(double.IsNaN(Number(Invoke(date, "getTime"))));
    }

    [Fact]
    public void ToPrimitive()
    {
        using var context = CreateContext();
        var date = NewDate(context, 0);
        Assert.Equal("Thu Jan 01 1970 01:00:00 GMT+0100 (CET)", Text(Invoke(date, WellKnownSymbols.ToPrimitive, "default")));
        Assert.Equal("Thu Jan 01 1970 01:00:00 GMT+0100 (CET)", Text(Invoke(date, WellKnownSymbols.ToPrimitive, "string")));
        Assert.Equal(0, Number(Invoke(date, WellKnownSymbols.ToPrimitive, "number")));
        context.AssertThrows(ErrorType.TypeError, () => Invoke(date, WellKnownSymbols.ToPrimitive, "invalid"));
        context.AssertThrows(ErrorType.TypeError, () => Invoke(date, WellKnownSymbols.ToPrimitive));
        var method = FundamentalsTestContext.Get(context.Intrinsics.DatePrototype, WellKnownSymbols.ToPrimitive);
        context.AssertThrows(ErrorType.TypeError, () => JsOperations.Call(method, 1, ["number"]));

        // A generic object
        var obj = context.NewObject();
        obj.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) => 42));
        Assert.Equal(42, Number(JsOperations.Call(method, obj, ["number"])));
        Assert.Equal("[object Object]", Text(JsOperations.Call(method, obj, ["string"])));
    }

    [Fact]
    public void ToJson()
    {
        using var context = CreateContext();
        Assert.Equal("1970-01-01T00:00:00.000Z", Text(Invoke(NewDate(context, 0), "toJSON")));
        Assert.True(Invoke(NewDate(context, double.NaN), "toJSON").IsNull);

        var obj = context.NewObject();
        obj.CreateDataPropertyOrThrow("valueOf", context.Function((_, _) => 1));
        obj.CreateDataPropertyOrThrow("toISOString", context.Function((_, _) => "custom"));
        Assert.Equal("custom", Text(context.CallPrototypeMethod("Date", "toJSON", obj)));
    }

    [Fact]
    public void TimeZone_FromTheTimeProvider()
    {
        var windowsZone = TimeZoneInfo.CreateCustomTimeZone("Romance Standard Time", TimeSpan.FromHours(1), "Romance", "Romance");
        using (var context = new FundamentalsTestContext(new JsEngineOptions { TimeProvider = new FixedTimeProvider(FixedNow, windowsZone) }))
        {
            Assert.Equal("Europe/Paris", context.Engine.SystemTimeZone.PrimaryIdentifier);
        }

        var ianaZone = TimeZoneInfo.CreateCustomTimeZone("Asia/Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo");
        using (var context = new FundamentalsTestContext(new JsEngineOptions { TimeProvider = new FixedTimeProvider(FixedNow, ianaZone) }))
        {
            Assert.Equal("Asia/Tokyo", context.Engine.SystemTimeZone.PrimaryIdentifier);
        }

        var unknownZone = TimeZoneInfo.CreateCustomTimeZone("My zone", TimeSpan.FromHours(9), "Mine", "Mine");
        using (var context = new FundamentalsTestContext(new JsEngineOptions { TimeProvider = new FixedTimeProvider(FixedNow, unknownZone) }))
        {
            Assert.Equal("UTC", context.Engine.SystemTimeZone.PrimaryIdentifier);
        }

        using (var context = new FundamentalsTestContext(new JsEngineOptions { TimeZone = "asia/calcutta" }))
        {
            Assert.Equal("Asia/Kolkata", context.Engine.SystemTimeZone.PrimaryIdentifier);
            Assert.Equal(-330, Number(Invoke(NewDate(context, 0), "getTimezoneOffset")));
        }

        using (var context = new FundamentalsTestContext(new JsEngineOptions { TimeZone = "Invalid/Zone" }))
        {
            Assert.Throws<InvalidOperationException>(() => context.Engine.SystemTimeZone);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo localTimeZone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }
}
