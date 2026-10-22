using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The fields that CreateDateTimeFormat requires (the required argument of CreateDateTimeFormat and GetDateTimeFormat).</summary>
internal enum DateTimeRequired
{
    Date,
    Time,
    YearMonth,
    MonthDay,
    Any,
}

/// <summary>The fields that CreateDateTimeFormat adds when none is requested (the defaults argument).</summary>
internal enum DateTimeDefaults
{
    Date,
    Time,
    YearMonth,
    MonthDay,
    ZonedDateTime,
    All,
}

/// <summary>The hour cycles of a locale ([[LocaleData]].[[hourCycle]], [[hourCycle12]] and [[hourCycle24]]).</summary>
internal static class DateTimeFormatLocaleData
{
    private static readonly ConcurrentDictionary<string, (string Default, string HourCycle12, string HourCycle24)> HourCycles = new(StringComparer.Ordinal);

    /// <summary>Gets the hour cycles of a locale from the time data of its region (preferred, then the first allowed 12-hour and 24-hour cycles).</summary>
    public static (string Default, string HourCycle12, string HourCycle24) GetHourCycles(string locale)
    {
        return HourCycles.GetOrAdd(locale, static locale =>
        {
            var supplemental = CldrData.Default.Supplemental;
            var tag = LanguageTag.Parse(locale) ?? LanguageTag.Parse("und")!;
            var region = LocaleInfo.GetRegionPreference(tag, region => supplemental.GetTimeData(region) is not null);
            var timeData = supplemental.GetTimeData(tag.Language + "-" + region) ?? supplemental.GetTimeData(tag.Language + "_" + region) ?? supplemental.GetTimeData(region) ?? supplemental.GetTimeData("001");
            var preferred = ToHourCycle(timeData?.Preferred) ?? "h23";
            string? hourCycle12 = null;
            string? hourCycle24 = null;
            if (timeData is not null)
            {
                foreach (var allowed in timeData.Allowed)
                {
                    var hourCycle = ToHourCycle(allowed);
                    if (hourCycle is "h11" or "h12")
                    {
                        hourCycle12 ??= hourCycle;
                    }
                    else if (hourCycle is "h23" or "h24")
                    {
                        hourCycle24 ??= hourCycle;
                    }
                }
            }

            if (preferred is "h11" or "h12")
            {
                hourCycle12 = preferred;
            }
            else
            {
                hourCycle24 = preferred;
            }

            return (preferred, hourCycle12 ?? "h12", hourCycle24 ?? "h23");
        });
    }

    /// <summary>Gets the preferred hour cycle of a locale.</summary>
    public static string GetDefaultHourCycle(string locale) => GetHourCycles(locale).Default;

    private static string? ToHourCycle(string? symbol) => string.IsNullOrEmpty(symbol) ? null : DateTimePattern.GetHourCycle(symbol[0]);
}

/// <summary>Intl.DateTimeFormat (https://tc39.es/ecma402/#datetimeformat-objects), with the amendments of Temporal.</summary>
internal static class DateTimeFormatBuiltins
{
    private static readonly string[] RelevantExtensionKeys = ["ca", "hc", "nu"];
    private static readonly string[] HourCycleValues = ["h11", "h12", "h23", "h24"];
    private static readonly string[] Styles = ["full", "long", "medium", "short"];
    private static readonly string[] DateFields = ["weekday", "era", "year", "month", "day"];
    private static readonly string[] YearMonthFields = ["era", "year", "month"];
    private static readonly string[] MonthDayFields = ["month", "day"];
    private static readonly string[] TimeFields = ["dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"];
    private static readonly string[] DateTimeFields = ["weekday", "era", "year", "month", "day", "dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"];
    private static readonly string[] AnyPresentFields = ["weekday", "year", "month", "day", "dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"];

    // The rows of the table "Components of date and time formats": the property and its values
    private static readonly (string Property, string[] Values)[] Components =
    [
        ("weekday", ["narrow", "short", "long"]),
        ("era", ["narrow", "short", "long"]),
        ("year", ["2-digit", "numeric"]),
        ("month", ["2-digit", "numeric", "narrow", "short", "long"]),
        ("day", ["2-digit", "numeric"]),
        ("dayPeriod", ["narrow", "short", "long"]),
        ("hour", ["2-digit", "numeric"]),
        ("minute", ["2-digit", "numeric"]),
        ("second", ["2-digit", "numeric"]),
        ("fractionalSecondDigits", []),
        ("timeZoneName", ["short", "long", "shortOffset", "longOffset", "shortGeneric", "longGeneric"]),
    ];

    private static readonly ConcurrentDictionary<(DateTimeLocaleData Data, string Key), DateTimeFormatRecord> Formats = new();
    private static readonly ConcurrentDictionary<(string Locale, string Key), IReadOnlyList<string?>> LocaleData = new();
    private static readonly string?[] HourCycleLocaleData = [null, "h11", "h12", "h23", "h24"];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("formatToParts", 1, FormatToParts),
        new("formatRange", 2, FormatRange),
        new("formatRangeToParts", 2, FormatRangeToParts),
        new("resolvedOptions", 0, ResolvedOptions),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IntlDateTimeFormatPrototype;
        var constructor = realm.CreateBuiltinFunction("DateTimeFormat", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.DateTimeFormat", PropertyFlags.Configurable);
        realm.DefineAccessor(prototype, "format", GetFormat, setter: null);
        realm.DefineMethods(prototype, PrototypeMethods);
        intl.DefineBuiltIn("DateTimeFormat", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.DateTimeFormat([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget ?? function, static i => i.IntlDateTimeFormatPrototype);
        var dateTimeFormat = CreateDateTimeFormat(prototype, arguments.Length > 0 ? arguments[0] : default, arguments.Length > 1 ? arguments[1] : default, DateTimeRequired.Any, DateTimeDefaults.Date);

        // ChainDateTimeFormat (the normative optional constructor mode)
        if (newTarget is null && thisValue.TryGetObject() is { } thisObject && ObjectOperations.OrdinaryHasInstance(function.Realm.Intrinsics.IntlDateTimeFormat, thisValue))
        {
            thisObject.DefinePropertyOrThrow(function.Realm.Intrinsics.IntlFallbackSymbol, PropertyDescriptor.Data(dateTimeFormat, PropertyFlags.None));
            return thisObject;
        }

        return dateTimeFormat;
    }

    /// <summary>The locale data of the relevant extension keys.</summary>
    private static IReadOnlyList<string?> GetLocaleData(string locale, string key)
    {
        if (key is "hc")
            return HourCycleLocaleData;

        return LocaleData.GetOrAdd((locale, key), static key =>
        {
            if (key.Key is "nu")
                return NumberFormatBuiltins.GetNumberingSystemLocaleData(key.Locale);

            var tag = LanguageTag.Parse(key.Locale) ?? LanguageTag.Parse("und")!;
            var preferred = LocaleInfo.GetCalendars(tag);
            var result = new List<string?>(LocaleInfo.AvailableCalendars.Length + 1) { preferred[0] };
            foreach (var calendar in LocaleInfo.AvailableCalendars)
            {
                if (calendar != preferred[0])
                {
                    result.Add(calendar);
                }
            }

            return result;
        });
    }

    /// <summary>CreateDateTimeFormat(newTarget, locales, options, required, defaults [, toLocaleStringTimeZone])</summary>
    internal static JsDateTimeFormatObject CreateDateTimeFormat(JsObject prototype, JsValue locales, JsValue optionsValue, DateTimeRequired required, DateTimeDefaults defaults, TemporalTimeZone? toLocaleStringTimeZone = null)
    {
        var dateTimeFormat = new JsDateTimeFormatObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(locales);
        var options = IntlOperations.CoerceOptionsToObject(optionsValue);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var calendar = IntlOperations.GetStringOption(options, "calendar", [], fallback: null);
        if (calendar is not null && !LanguageTag.IsUnicodeType(calendar))
            ThrowHelper.ThrowRangeError("Invalid calendar : " + calendar);

        var numberingSystem = IntlOperations.GetStringOption(options, "numberingSystem", [], fallback: null);
        if (numberingSystem is not null && !LanguageTag.IsUnicodeType(numberingSystem))
            ThrowHelper.ThrowRangeError("Invalid numberingSystem : " + numberingSystem);

        var hour12 = IntlOperations.GetBooleanOption(options, "hour12", fallback: null);
        var hourCycle = IntlOperations.GetStringOption(options, "hourCycle", HourCycleValues, fallback: null);
        if (hour12 is not null)
        {
            hourCycle = null;
        }

        var r = IntlOperations.ResolveLocale(requestedLocales, RelevantExtensionKeys, key => key switch
        {
            "ca" => calendar,
            "nu" => numberingSystem,
            _ => hourCycle,
        }, GetLocaleData);

        var locale = r.Locale;
        if (hour12 is not null && locale.Contains("-u-", StringComparison.Ordinal) && LanguageTag.Parse(locale) is { } localeTag && localeTag.GetKeyword("hc") is not null)
        {
            // [[hc]] is null when hour12 is present: the hc keyword is not supported
            localeTag.Keywords.RemoveAll(keyword => keyword.Key == "hc");
            if (localeTag.Keywords.Count == 0 && localeTag.Attributes.Count == 0)
            {
                localeTag.HasUnicodeExtension = false;
            }

            locale = localeTag.ToString();
        }

        dateTimeFormat.Locale = locale;
        var resolvedCalendar = r.Get("ca") ?? "gregory";
        dateTimeFormat.Calendar = resolvedCalendar;
        dateTimeFormat.NumberingSystem = r.Get("nu") ?? "latn";
        var cldrLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        var data = DateTimeLocaleData.Get(cldrLocale, resolvedCalendar);
        dateTimeFormat.Data = data;
        var hourCycles = DateTimeFormatLocaleData.GetHourCycles(r.DataLocale);
        string hc;
        if (hour12 is true)
        {
            hc = hourCycles.HourCycle12;
        }
        else if (hour12 is false)
        {
            hc = hourCycles.HourCycle24;
        }
        else
        {
            hc = r.Get("hc") ?? hourCycles.Default;
        }

        dateTimeFormat.HourCycle = hc;

        var timeZoneValue = options.Get("timeZone");
        string timeZoneText;
        if (timeZoneValue.IsUndefined)
        {
            timeZoneText = toLocaleStringTimeZone?.Identifier ?? TemporalOperations.SystemTimeZoneIdentifier(JsEngine.Current!).Identifier;
        }
        else
        {
            if (toLocaleStringTimeZone is not null)
                ThrowHelper.ThrowTypeError("The timeZone option is not allowed by Temporal.ZonedDateTime.prototype.toLocaleString");

            timeZoneText = JsOperations.ToString(timeZoneValue);
        }

        SetTimeZone(dateTimeFormat, timeZoneText, toLocaleStringTimeZone);

        var formatOptions = new DateTimeComponents { HourCycle = hc };
        var hasExplicitFormatComponents = false;
        foreach (var (property, values) in Components)
        {
            object? value;
            if (property is "fractionalSecondDigits")
            {
                value = IntlOperations.GetNumberOption(options, "fractionalSecondDigits", 1, 3, fallback: null);
            }
            else
            {
                value = IntlOperations.GetStringOption(options, property, values, fallback: null);
            }

            formatOptions.Set(property, value);
            if (value is not null)
            {
                hasExplicitFormatComponents = true;
            }
        }

        var formatMatcher = IntlOperations.GetStringOption(options, "formatMatcher", ["basic", "best fit"], "best fit")!;
        var dateStyle = IntlOperations.GetStringOption(options, "dateStyle", Styles, fallback: null);
        dateTimeFormat.DateStyle = dateStyle;
        var timeStyle = IntlOperations.GetStringOption(options, "timeStyle", Styles, fallback: null);
        dateTimeFormat.TimeStyle = timeStyle;
        DateTimeFormatRecord bestFormat;
        if (dateStyle is not null || timeStyle is not null)
        {
            if (hasExplicitFormatComponents)
                ThrowHelper.ThrowTypeError("dateStyle and timeStyle cannot be used with the date and time component options");

            if (required is DateTimeRequired.Date && timeStyle is not null)
                ThrowHelper.ThrowTypeError("timeStyle is not allowed");

            if (required is DateTimeRequired.Time && dateStyle is not null)
                ThrowHelper.ThrowTypeError("dateStyle is not allowed");

            bestFormat = DateTimeStyleFormat(data, dateStyle, timeStyle, hc);
            if (dateStyle is not null)
            {
                dateTimeFormat.PlainDateFormat = AdjustDateTimeStyleFormat(data, bestFormat, DateFields, hc);
                dateTimeFormat.PlainYearMonthFormat = AdjustDateTimeStyleFormat(data, bestFormat, YearMonthFields, hc);
                dateTimeFormat.PlainMonthDayFormat = AdjustDateTimeStyleFormat(data, bestFormat, MonthDayFields, hc);
            }

            if (timeStyle is not null)
            {
                dateTimeFormat.PlainTimeFormat = AdjustDateTimeStyleFormat(data, bestFormat, TimeFields, hc);
            }

            dateTimeFormat.PlainDateTimeFormat = AdjustDateTimeStyleFormat(data, bestFormat, DateTimeFields, hc);
            dateTimeFormat.InstantFormat = bestFormat;
        }
        else
        {
            bestFormat = GetDateTimeFormat(data, formatOptions, required, defaults, inheritAll: true)!;
            dateTimeFormat.PlainDateFormat = GetDateTimeFormat(data, formatOptions, DateTimeRequired.Date, DateTimeDefaults.Date, inheritAll: false);
            dateTimeFormat.PlainYearMonthFormat = GetDateTimeFormat(data, formatOptions, DateTimeRequired.YearMonth, DateTimeDefaults.YearMonth, inheritAll: false);
            dateTimeFormat.PlainMonthDayFormat = GetDateTimeFormat(data, formatOptions, DateTimeRequired.MonthDay, DateTimeDefaults.MonthDay, inheritAll: false);
            dateTimeFormat.PlainTimeFormat = GetDateTimeFormat(data, formatOptions, DateTimeRequired.Time, DateTimeDefaults.Time, inheritAll: false);
            dateTimeFormat.PlainDateTimeFormat = GetDateTimeFormat(data, formatOptions, DateTimeRequired.Any, DateTimeDefaults.All, inheritAll: false)!;
            dateTimeFormat.InstantFormat = toLocaleStringTimeZone is not null
                ? GetDateTimeFormat(data, formatOptions, DateTimeRequired.Any, DateTimeDefaults.ZonedDateTime, inheritAll: true)!
                : GetDateTimeFormat(data, formatOptions, DateTimeRequired.Any, DateTimeDefaults.All, inheritAll: true)!;
        }

        dateTimeFormat.Format = bestFormat;
        return dateTimeFormat;
    }

    private static void SetTimeZone(JsDateTimeFormatObject dateTimeFormat, string timeZone, TemporalTimeZone? toLocaleStringTimeZone)
    {
        if (toLocaleStringTimeZone is not null && ReferenceEquals(timeZone, toLocaleStringTimeZone.Identifier))
        {
            dateTimeFormat.TimeZoneIdentifier = toLocaleStringTimeZone.Identifier;
            dateTimeFormat.TimeZone = toLocaleStringTimeZone;
            return;
        }

        if (timeZone.Length > 0 && timeZone[0] is '+' or '-' or '\u2212')
        {
            // IsTimeZoneOffsetString: an offset with seconds is a RangeError
            if (!TemporalParser.TryParseDateTimeUtcOffset(timeZone, out var offsetNanoseconds, out var hasSeconds) || hasSeconds)
                ThrowHelper.ThrowRangeError("Invalid time zone specified: " + timeZone);

            var offsetMinutes = (int)(offsetNanoseconds / 60_000_000_000);
            var zone = TemporalTimeZone.FromOffset(offsetMinutes);
            dateTimeFormat.TimeZoneIdentifier = zone.Identifier;
            dateTimeFormat.TimeZone = zone;
            return;
        }

        if (!TimeZoneDatabase.Default.TryGetTimeZone(timeZone, out var record))
            ThrowHelper.ThrowRangeError("Invalid time zone specified: " + timeZone);

        var temporalTimeZone = TemporalTimeZone.FromNamed(record);
        dateTimeFormat.TimeZoneIdentifier = temporalTimeZone.Identifier;
        dateTimeFormat.TimeZone = temporalTimeZone;
    }

    /// <summary>GetDateTimeFormat(formats, matcher, options, required, defaults, inherit): the best format, or null.</summary>
    private static DateTimeFormatRecord? GetDateTimeFormat(DateTimeLocaleData data, DateTimeComponents options, DateTimeRequired required, DateTimeDefaults defaults, bool inheritAll)
    {
        ReadOnlySpan<string> requiredOptions = required switch
        {
            DateTimeRequired.Date => ["weekday", "year", "month", "day"],
            DateTimeRequired.Time => ["dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"],
            DateTimeRequired.YearMonth => ["year", "month"],
            DateTimeRequired.MonthDay => ["month", "day"],
            _ => ["weekday", "year", "month", "day", "dayPeriod", "hour", "minute", "second", "fractionalSecondDigits"],
        };

        ReadOnlySpan<string> defaultOptions = defaults switch
        {
            DateTimeDefaults.Date => ["year", "month", "day"],
            DateTimeDefaults.Time => ["hour", "minute", "second"],
            DateTimeDefaults.YearMonth => ["year", "month"],
            DateTimeDefaults.MonthDay => ["month", "day"],
            _ => ["year", "month", "day", "hour", "minute", "second"],
        };

        DateTimeComponents formatOptions;
        if (inheritAll)
        {
            formatOptions = options.Clone();
        }
        else
        {
            formatOptions = new DateTimeComponents();
            if (required is DateTimeRequired.Date or DateTimeRequired.YearMonth or DateTimeRequired.Any)
            {
                formatOptions.Era = options.Era;
            }

            if (required is DateTimeRequired.Time or DateTimeRequired.Any)
            {
                formatOptions.HourCycle = options.HourCycle;
            }
        }

        var anyPresent = false;
        foreach (var property in AnyPresentFields)
        {
            if (options.Get(property) is not null)
            {
                anyPresent = true;
            }
        }

        var needDefaults = true;
        foreach (var property in requiredOptions)
        {
            var value = options.Get(property);
            if (value is not null)
            {
                formatOptions.Set(property, value);
                needDefaults = false;
            }
        }

        if (needDefaults)
        {
            if (anyPresent && !inheritAll)
                return null;

            foreach (var property in defaultOptions)
            {
                formatOptions.Set(property, "numeric");
            }

            if (defaults is DateTimeDefaults.ZonedDateTime && formatOptions.TimeZoneName is null)
            {
                formatOptions.TimeZoneName = "short";
            }
        }

        return GetBestFormat(data, formatOptions, options.HourCycle);
    }

    /// <summary>AdjustDateTimeStyleFormat(formats, baseFormat, matcher, allowedOptions)</summary>
    private static DateTimeFormatRecord AdjustDateTimeStyleFormat(DateTimeLocaleData data, DateTimeFormatRecord baseFormat, string[] allowedOptions, string hourCycle)
    {
        var anyConflictingFields = false;
        foreach (var (property, _) in Components)
        {
            if (baseFormat.Components.Get(property) is not null && Array.IndexOf(allowedOptions, property) < 0)
            {
                anyConflictingFields = true;
            }
        }

        if (!anyConflictingFields)
            return baseFormat;

        var formatOptions = new DateTimeComponents();
        foreach (var property in allowedOptions)
        {
            if (baseFormat.Components.Get(property) is { } value)
            {
                formatOptions.Set(property, value);
            }
        }

        return GetBestFormat(data, formatOptions, hourCycle);
    }

    // BestFitFormatMatcher (also used for "basic": the patterns of the locale are generated, not enumerated)
    private static DateTimeFormatRecord GetBestFormat(DateTimeLocaleData data, DateTimeComponents options, string? hourCycle)
    {
        var skeleton = options.ToSkeleton(hourCycle);
        return Formats.GetOrAdd((data, skeleton + "|" + hourCycle), static (key, hourCycle) =>
        {
            var pattern = key.Data.Generator.GetBestPattern(key.Key[..key.Key.IndexOf('|', StringComparison.Ordinal)]);
            if (DateTimePattern.GetHourCycleOfPattern(pattern) is not null)
            {
                pattern = DateTimePattern.ReplaceHourCycle(pattern, hourCycle);
            }

            return new DateTimeFormatRecord(key.Data, pattern);
        }, hourCycle);
    }

    /// <summary>DateTimeStyleFormat(dateStyle, timeStyle, styles): the pattern of the styles, with the hour cycle of the format.</summary>
    private static DateTimeFormatRecord DateTimeStyleFormat(DateTimeLocaleData data, string? dateStyle, string? timeStyle, string hourCycle)
    {
        return Formats.GetOrAdd((data, "style|" + dateStyle + "|" + timeStyle + "|" + hourCycle), static (key, state) =>
        {
            var (dateStyle, timeStyle, hourCycle) = state;
            var data = key.Data;
            string pattern;
            if (dateStyle is not null && timeStyle is not null)
            {
                pattern = DateTimePatternGenerator.CombineDateTime(data.GetDateTimeFormat(dateStyle), data.GetDateFormat(dateStyle), data.GetTimeFormat(timeStyle));
            }
            else if (timeStyle is not null)
            {
                pattern = data.GetTimeFormat(timeStyle);
            }
            else
            {
                pattern = data.GetDateFormat(dateStyle!);
            }

            if (timeStyle is not null && DateTimePattern.GetHourCycleOfPattern(data.GetTimeFormat(timeStyle)) != hourCycle)
            {
                // The pattern of another hour cycle: the pattern of its skeleton with the hour of the hour cycle (as V8)
                var skeleton = ReplaceHourInSkeleton(DateTimeSkeleton.Create(pattern).GetSkeleton(), hourCycle);
                pattern = DateTimePattern.ReplaceHourCycle(data.Generator.GetBestPattern(skeleton), hourCycle);
            }

            var numberOverrides = dateStyle is null ? null : ParseNumberOverrides(data.GetString("dateFormats/" + dateStyle + "-numbers"));
            return new DateTimeFormatRecord(data, pattern) { DateStyle = dateStyle, TimeStyle = timeStyle, NumberOverrides = numberOverrides };
        }, (dateStyle, timeStyle, hourCycle));
    }

    // The "numbers" attribute of a date pattern: "hebr" (every date field) or "d=hanidays;y=..." (the fields of the letters)
    private static Dictionary<char, string>? ParseNumberOverrides(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var result = new Dictionary<char, string>();
        foreach (var item in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                foreach (var symbol in "GyYuUrQqMLwWdDFEecg")
                {
                    result[symbol] = item;
                }
            }
            else
            {
                foreach (var symbol in item.AsSpan(0, separator))
                {
                    result[symbol] = item[(separator + 1)..];
                }
            }
        }

        return result;
    }

    // The skeleton with the hour letter of an hour cycle, without the day periods (V8's ReplaceSkeleton)
    private static string ReplaceHourInSkeleton(string skeleton, string hourCycle)
    {
        var hourSymbol = DateTimePattern.GetHourSymbol(hourCycle);
        var result = new StringBuilder(skeleton.Length);
        foreach (var c in skeleton)
        {
            switch (c)
            {
                case 'a' or 'b' or 'B':
                    break;
                case 'h' or 'H' or 'K' or 'k':
                    result.Append(hourSymbol);
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>UnwrapDateTimeFormat(dtf) then RequireInternalSlot([[InitializedDateTimeFormat]])</summary>
    private static JsDateTimeFormatObject UnwrapDateTimeFormat(JsBuiltinFunction function, JsValue value, string method)
    {
        if (value.TryGetObject() is not { } obj)
            return ThrowHelper.ThrowTypeError<JsDateTimeFormatObject>($"Intl.DateTimeFormat.prototype.{method} called on an incompatible receiver");

        if (obj is not JsDateTimeFormatObject && ObjectOperations.OrdinaryHasInstance(function.Realm.Intrinsics.IntlDateTimeFormat, value))
        {
            value = obj.Get(function.Realm.Intrinsics.IntlFallbackSymbol);
        }

        return ThisDateTimeFormat(value, method);
    }

    private static JsDateTimeFormatObject ThisDateTimeFormat(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsDateTimeFormatObject dateTimeFormat)
            return dateTimeFormat;

        return ThrowHelper.ThrowTypeError<JsDateTimeFormatObject>($"Intl.DateTimeFormat.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>IsTemporalObject(value)</summary>
    private static bool IsTemporalObject(JsValue value) => value.Cell is JsPlainDate or JsPlainTime or JsPlainDateTime or JsZonedDateTime or JsPlainYearMonth or JsPlainMonthDay or JsInstant;

    /// <summary>ToDateTimeFormattable(value)</summary>
    private static JsValue ToDateTimeFormattable(JsValue value)
    {
        if (IsTemporalObject(value))
            return value;

        return JsOperations.ToNumber(value);
    }

    /// <summary>The value to format when the argument is undefined: Date.now().</summary>
    private static JsValue GetDateArgument(JsBuiltinFunction function, ReadOnlySpan<JsValue> arguments)
    {
        var date = arguments.Length > 0 ? arguments[0] : default;
        if (date.IsUndefined)
            return function.Realm.Engine.GetCurrentTimeValue();

        return ToDateTimeFormattable(date);
    }

    /// <summary>HandleDateTimeValue(dateTimeFormat, x): the format, the epoch nanoseconds and whether the value is a plain (wall-clock) value.</summary>
    private static (DateTimeFormatRecord Format, Int128 EpochNanoseconds, bool IsPlain) HandleDateTimeValue(JsDateTimeFormatObject dtf, JsValue x)
    {
        switch (x.Cell)
        {
            case JsPlainDate date:
                if (date.Calendar != dtf.Calendar && date.Calendar != TemporalOperations.IsoCalendar)
                    ThrowHelper.ThrowRangeError("The calendar of the Temporal.PlainDate (" + date.Calendar + ") is not the calendar of the DateTimeFormat (" + dtf.Calendar + ")");

                return (dtf.PlainDateFormat ?? ThrowHelper.ThrowTypeError<DateTimeFormatRecord>("The options of the DateTimeFormat do not apply to a Temporal.PlainDate"), TemporalOperations.GetUtcEpochNanoseconds(new IsoDateTime(date.IsoDate, TimeRecord.Noon)), true);

            case JsPlainYearMonth yearMonth:
                if (yearMonth.Calendar != dtf.Calendar)
                    ThrowHelper.ThrowRangeError("The calendar of the Temporal.PlainYearMonth (" + yearMonth.Calendar + ") is not the calendar of the DateTimeFormat (" + dtf.Calendar + ")");

                return (dtf.PlainYearMonthFormat ?? ThrowHelper.ThrowTypeError<DateTimeFormatRecord>("The options of the DateTimeFormat do not apply to a Temporal.PlainYearMonth"), TemporalOperations.GetUtcEpochNanoseconds(new IsoDateTime(yearMonth.IsoDate, TimeRecord.Noon)), true);

            case JsPlainMonthDay monthDay:
                if (monthDay.Calendar != dtf.Calendar)
                    ThrowHelper.ThrowRangeError("The calendar of the Temporal.PlainMonthDay (" + monthDay.Calendar + ") is not the calendar of the DateTimeFormat (" + dtf.Calendar + ")");

                return (dtf.PlainMonthDayFormat ?? ThrowHelper.ThrowTypeError<DateTimeFormatRecord>("The options of the DateTimeFormat do not apply to a Temporal.PlainMonthDay"), TemporalOperations.GetUtcEpochNanoseconds(new IsoDateTime(monthDay.IsoDate, TimeRecord.Noon)), true);

            case JsPlainTime time:
                return (dtf.PlainTimeFormat ?? ThrowHelper.ThrowTypeError<DateTimeFormatRecord>("The options of the DateTimeFormat do not apply to a Temporal.PlainTime"), TemporalOperations.GetUtcEpochNanoseconds(new IsoDateTime(new IsoDate(1970, 1, 1), time.Time)), true);

            case JsPlainDateTime dateTime:
                if (dateTime.Calendar != dtf.Calendar && dateTime.Calendar != TemporalOperations.IsoCalendar)
                    ThrowHelper.ThrowRangeError("The calendar of the Temporal.PlainDateTime (" + dateTime.Calendar + ") is not the calendar of the DateTimeFormat (" + dtf.Calendar + ")");

                return (dtf.PlainDateTimeFormat, TemporalOperations.GetUtcEpochNanoseconds(dateTime.IsoDateTime), true);

            case JsInstant instant:
                return (dtf.InstantFormat, instant.EpochNanoseconds, false);

            case JsZonedDateTime:
                return ThrowHelper.ThrowTypeError<(DateTimeFormatRecord, Int128, bool)>("Temporal.ZonedDateTime is not supported by Intl.DateTimeFormat: use toLocaleString");

            default:
                {
                    // HandleDateTimeOthers
                    var time = DateOperations.TimeClip(x.NumberValue);
                    if (double.IsNaN(time))
                        ThrowHelper.ThrowRangeError("Invalid time value");

                    return (dtf.Format, (Int128)(long)time * 1_000_000, false);
                }
        }
    }

    /// <summary>PartitionDateTimePattern(dateTimeFormat, x)</summary>
    internal static List<DateTimePart> PartitionDateTimePattern(JsDateTimeFormatObject dtf, JsValue x)
    {
        var (format, epochNanoseconds, isPlain) = HandleDateTimeValue(dtf, x);
        var result = new List<DateTimePart>();
        DateTimeFormatter.FormatPattern(dtf, format, epochNanoseconds, isPlain, result);
        return result;
    }

    /// <summary>FormatDateTime(dateTimeFormat, x)</summary>
    internal static string FormatDateTime(JsDateTimeFormatObject dtf, JsValue x)
    {
        var parts = PartitionDateTimePattern(dtf, x);
        if (parts.Count == 1)
            return parts[0].Value;

        var result = new StringBuilder();
        foreach (var part in parts)
        {
            result.Append(part.Value);
        }

        return result.ToString();
    }

    /// <summary>get Intl.DateTimeFormat.prototype.format</summary>
    private static JsValue GetFormat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dtf = UnwrapDateTimeFormat(function, thisValue, "format");
        if (dtf.BoundFormat is null)
        {
            var bound = function.Realm.CreateBuiltinFunction("", 1, static (f, t, a, n) =>
            {
                var dateTimeFormat = (JsDateTimeFormatObject)f.State!;
                return FormatDateTime(dateTimeFormat, GetDateArgument(f, a));
            });
            bound.State = dtf;
            dtf.BoundFormat = bound;
        }

        return dtf.BoundFormat;
    }

    /// <summary>Intl.DateTimeFormat.prototype.formatToParts(date)</summary>
    private static JsValue FormatToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dtf = ThisDateTimeFormat(thisValue, "formatToParts");
        var x = GetDateArgument(function, arguments);
        var parts = PartitionDateTimePattern(dtf, x);
        return CreatePartsArray(function.Realm, parts, includeSource: false);
    }

    private static JsArray CreatePartsArray(JsRealm realm, List<DateTimePart> parts, bool includeSource)
    {
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Value);
            if (includeSource)
            {
                o.CreateDataPropertyOrThrow("source", parts[i].Source);
            }

            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    private static (JsValue X, JsValue Y) GetRangeArguments(ReadOnlySpan<JsValue> arguments)
    {
        var start = arguments.Length > 0 ? arguments[0] : default;
        var end = arguments.Length > 1 ? arguments[1] : default;
        if (start.IsUndefined || end.IsUndefined)
            ThrowHelper.ThrowTypeError("startDate and endDate are required");

        var x = ToDateTimeFormattable(start);
        var y = ToDateTimeFormattable(end);
        return (x, y);
    }

    /// <summary>PartitionDateTimeRangePattern(dateTimeFormat, x, y)</summary>
    private static List<DateTimePart> PartitionDateTimeRangePattern(JsDateTimeFormatObject dtf, JsValue x, JsValue y)
    {
        if ((IsTemporalObject(x) || IsTemporalObject(y)) && !SameTemporalType(x, y))
            ThrowHelper.ThrowTypeError("The start and end of the range must be of the same type");

        var (format, xEpochNanoseconds, isPlain) = HandleDateTimeValue(dtf, x);
        var (_, yEpochNanoseconds, _) = HandleDateTimeValue(dtf, y);
        return DateIntervalFormatter.Format(dtf, format, xEpochNanoseconds, yEpochNanoseconds, isPlain);
    }

    /// <summary>SameTemporalType(x, y)</summary>
    private static bool SameTemporalType(JsValue x, JsValue y)
    {
        if (!IsTemporalObject(x) || !IsTemporalObject(y))
            return false;

        return x.Cell!.GetType() == y.Cell!.GetType();
    }

    /// <summary>Intl.DateTimeFormat.prototype.formatRange(startDate, endDate)</summary>
    private static JsValue FormatRange(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dtf = ThisDateTimeFormat(thisValue, "formatRange");
        var (x, y) = GetRangeArguments(arguments);
        var result = new StringBuilder();
        foreach (var part in PartitionDateTimeRangePattern(dtf, x, y))
        {
            result.Append(part.Value);
        }

        return result.ToString();
    }

    /// <summary>Intl.DateTimeFormat.prototype.formatRangeToParts(startDate, endDate)</summary>
    private static JsValue FormatRangeToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dtf = ThisDateTimeFormat(thisValue, "formatRangeToParts");
        var (x, y) = GetRangeArguments(arguments);
        return CreatePartsArray(function.Realm, PartitionDateTimeRangePattern(dtf, x, y), includeSource: true);
    }

    /// <summary>Intl.DateTimeFormat.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dtf = UnwrapDateTimeFormat(function, thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", dtf.Locale);
        options.CreateDataPropertyOrThrow("calendar", dtf.Calendar);
        options.CreateDataPropertyOrThrow("numberingSystem", dtf.NumberingSystem);
        options.CreateDataPropertyOrThrow("timeZone", dtf.TimeZoneIdentifier);
        var format = dtf.Format;
        if (format.HasHour && dtf.HourCycle is { } hourCycle)
        {
            options.CreateDataPropertyOrThrow("hourCycle", hourCycle);
            options.CreateDataPropertyOrThrow("hour12", hourCycle is "h11" or "h12");
        }

        if (dtf.DateStyle is null && dtf.TimeStyle is null)
        {
            foreach (var (property, _) in Components)
            {
                switch (format.Components.Get(property))
                {
                    case string value:
                        options.CreateDataPropertyOrThrow(property, value);
                        break;
                    case int value:
                        options.CreateDataPropertyOrThrow(property, value);
                        break;
                }
            }
        }

        if (dtf.DateStyle is { } dateStyle)
        {
            options.CreateDataPropertyOrThrow("dateStyle", dateStyle);
        }

        if (dtf.TimeStyle is { } timeStyle)
        {
            options.CreateDataPropertyOrThrow("timeStyle", timeStyle);
        }

        return options;
    }
}
