namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The values of the date and time format components of an options record (the formatOptions of CreateDateTimeFormat).</summary>
internal sealed class DateTimeComponents
{
    public string? Weekday { get; set; }

    public string? Era { get; set; }

    public string? Year { get; set; }

    public string? Month { get; set; }

    public string? Day { get; set; }

    public string? DayPeriod { get; set; }

    public string? Hour { get; set; }

    public string? Minute { get; set; }

    public string? Second { get; set; }

    public int? FractionalSecondDigits { get; set; }

    public string? TimeZoneName { get; set; }

    /// <summary>Gets or sets the hour cycle ([[hourCycle]] of formatOptions).</summary>
    public string? HourCycle { get; set; }

    public DateTimeComponents Clone() => (DateTimeComponents)MemberwiseClone();

    /// <summary>Gets the value of a component by the name of its property (e.g. "weekday").</summary>
    public object? Get(string property) => property switch
    {
        "weekday" => Weekday,
        "era" => Era,
        "year" => Year,
        "month" => Month,
        "day" => Day,
        "dayPeriod" => DayPeriod,
        "hour" => Hour,
        "minute" => Minute,
        "second" => Second,
        "fractionalSecondDigits" => FractionalSecondDigits,
        "timeZoneName" => TimeZoneName,
        _ => null,
    };

    /// <summary>Sets the value of a component by the name of its property.</summary>
    public void Set(string property, object? value)
    {
        switch (property)
        {
            case "weekday": Weekday = (string?)value; break;
            case "era": Era = (string?)value; break;
            case "year": Year = (string?)value; break;
            case "month": Month = (string?)value; break;
            case "day": Day = (string?)value; break;
            case "dayPeriod": DayPeriod = (string?)value; break;
            case "hour": Hour = (string?)value; break;
            case "minute": Minute = (string?)value; break;
            case "second": Second = (string?)value; break;
            case "fractionalSecondDigits": FractionalSecondDigits = (int?)value; break;
            case "timeZoneName": TimeZoneName = (string?)value; break;
        }
    }

    /// <summary>
    /// Gets the skeleton of the components (as V8): the hour letter is the one of <paramref name="hourCycle"/> (e.g. "yMMMdjm"
    /// with h12 gives "yMMMdhm").
    /// </summary>
    public string ToSkeleton(string? hourCycle)
    {
        var result = new StringBuilder();
        Append(Weekday, "EEEEE", "EEE", "EEEE");
        Append(Era, "GGGGG", "G", "GGGG");
        switch (Year)
        {
            case "2-digit": result.Append("yy"); break;
            case "numeric": result.Append('y'); break;
        }

        switch (Month)
        {
            case "2-digit": result.Append("MM"); break;
            case "numeric": result.Append('M'); break;
            case "narrow": result.Append("MMMMM"); break;
            case "short": result.Append("MMM"); break;
            case "long": result.Append("MMMM"); break;
        }

        AppendNumeric(Day, 'd');
        Append(DayPeriod, "BBBBB", "B", "BBBB");
        if (Hour is not null)
        {
            AppendNumeric(Hour, DateTimePattern.GetHourSymbol(hourCycle ?? "h23"));
        }

        AppendNumeric(Minute, 'm');
        AppendNumeric(Second, 's');
        if (FractionalSecondDigits is { } digits)
        {
            result.Append('S', digits);
        }

        switch (TimeZoneName)
        {
            case "short": result.Append('z'); break;
            case "long": result.Append("zzzz"); break;
            case "shortOffset": result.Append('O'); break;
            case "longOffset": result.Append("OOOO"); break;
            case "shortGeneric": result.Append('v'); break;
            case "longGeneric": result.Append("vvvv"); break;
        }

        return result.ToString();

        void Append(string? value, string narrow, string shortValue, string longValue)
        {
            switch (value)
            {
                case "narrow": result.Append(narrow); break;
                case "short": result.Append(shortValue); break;
                case "long": result.Append(longValue); break;
            }
        }

        void AppendNumeric(string? value, char symbol)
        {
            switch (value)
            {
                case "2-digit": result.Append(symbol, 2); break;
                case "numeric": result.Append(symbol); break;
            }
        }
    }
}

/// <summary>
/// A DateTime Format Record: a pattern of UTS 35 and the values of the components it shows (derived from the pattern,
/// as reported by resolvedOptions). The patterns of the hour fields use the hour cycle of the DateTimeFormat.
/// </summary>
internal sealed class DateTimeFormatRecord
{
    private DateIntervalPatterns? _intervalPatterns;

    public DateTimeFormatRecord(DateTimeLocaleData data, string pattern)
    {
        Data = data;
        Pattern = pattern;
        Parts = DateTimePattern.Parse(pattern);
        foreach (var part in Parts)
        {
            if (part.IsLiteral)
                continue;

            var count = part.Count;
            switch (part.Symbol)
            {
                case 'E' or 'c' or 'e' when part.Symbol == 'E' || count >= 3:
                    Components.Weekday ??= count switch
                    {
                        4 => "long",
                        5 => "narrow",
                        _ => "short",
                    };
                    break;

                case 'G':
                    Components.Era ??= count switch
                    {
                        4 => "long",
                        5 => "narrow",
                        _ => "short",
                    };
                    break;

                case 'y' or 'Y' or 'u':
                    Components.Year ??= count == 2 ? "2-digit" : "numeric";
                    break;

                case 'r' or 'U':
                    Components.Year ??= "numeric";
                    break;

                case 'M' or 'L':
                    Components.Month ??= count switch
                    {
                        1 => "numeric",
                        2 => "2-digit",
                        3 => "short",
                        4 => "long",
                        _ => "narrow",
                    };
                    break;

                case 'd':
                    Components.Day ??= count == 2 ? "2-digit" : "numeric";
                    break;

                case 'B' or 'b':
                    Components.DayPeriod ??= count switch
                    {
                        4 => "long",
                        5 => "narrow",
                        _ => "short",
                    };
                    break;

                case 'h' or 'H' or 'k' or 'K':
                    Components.Hour ??= count == 2 ? "2-digit" : "numeric";
                    break;

                case 'm':
                    Components.Minute ??= count == 2 ? "2-digit" : "numeric";
                    break;

                case 's':
                    Components.Second ??= count == 2 ? "2-digit" : "numeric";
                    break;

                case 'S':
                    Components.FractionalSecondDigits ??= Math.Min(count, 3);
                    break;

                case 'z':
                    Components.TimeZoneName ??= count == 4 ? "long" : "short";
                    break;

                case 'O':
                    Components.TimeZoneName ??= count == 4 ? "longOffset" : "shortOffset";
                    break;

                case 'v':
                    Components.TimeZoneName ??= count == 4 ? "longGeneric" : "shortGeneric";
                    break;

                case 'V':
                    Components.TimeZoneName ??= count == 4 ? "longGeneric" : "shortGeneric";
                    break;

                case 'Z' or 'X' or 'x':
                    Components.TimeZoneName ??= count >= 4 ? "longOffset" : "shortOffset";
                    break;
            }
        }
    }

    public DateTimeLocaleData Data { get; }

    /// <summary>Gets the pattern (UTS 35).</summary>
    public string Pattern { get; }

    public DateTimePatternPart[] Parts { get; }

    /// <summary>Gets the values of the components shown by the pattern.</summary>
    public DateTimeComponents Components { get; } = new();

    public bool HasHour => Components.Hour is not null;

    /// <summary>Gets or sets the date style that created the pattern (for the date-time range of the styles), or null.</summary>
    public string? DateStyle { get; init; }

    /// <summary>Gets or sets the time style that created the pattern, or null.</summary>
    public string? TimeStyle { get; init; }

    /// <summary>Gets the interval patterns of the format (computed on first use).</summary>
    public DateIntervalPatterns GetIntervalPatterns(string? hourCycle)
    {
        return Volatile.Read(ref _intervalPatterns) ?? Meziantou.Framework.JavaScriptEngine.Intl.Data.CldrData.InitializeOnce(ref _intervalPatterns, DateIntervalPatterns.Create(this, hourCycle));
    }
}
