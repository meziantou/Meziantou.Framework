using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// Finds the best pattern of a locale for a skeleton (UTS 35 "Matching Skeletons"): the "best fit" format matcher of
/// ECMA-402. It follows ICU's DateTimePatternGenerator (the distance between the skeletons, the adjustment of the field
/// widths, the combination of a date and a time with dateTimeFormats and the appendItems for the missing fields), so
/// the patterns are the ones of the other engines. The instances are immutable and thread-safe.
/// </summary>
internal sealed class DateTimePatternGenerator
{
    private const int DateMask = (1 << (int)DateTimeSkeletonField.DayPeriod) - 1;
    private const int TimeMask = ((1 << DateTimeSkeleton.FieldCount) - 1) & ~DateMask;
    private const int SecondAndFractionalMask = (1 << (int)DateTimeSkeletonField.Second) | (1 << (int)DateTimeSkeletonField.FractionalSecond);
    private const int FractionalMask = 1 << (int)DateTimeSkeletonField.FractionalSecond;

    // The fields added one by one, as ICU (G y Q M w W E D F d a H m s S v)
    private const string CanonicalItems = "GyQMwWEDFdaHmsSv";

    private static readonly string?[] AppendItemNames = ["Era", "Year", "Quarter", "Month", "Week", null, "Day-Of-Week", null, null, "Day", null, "Hour", "Minute", "Second", null, "Timezone"];
    private static readonly string?[] FieldDisplayNames = ["era", "year", "quarter", "month", "week", "weekOfMonth", "weekday", "dayOfYear", "weekdayOfMonth", "day", "dayperiod", "hour", "minute", "second", null, "zone"];

    private readonly DateTimeLocaleData _data;
    private readonly List<Entry>?[] _buckets = new List<Entry>?[52];
    private readonly string?[] _appendItems = new string?[DateTimeSkeleton.FieldCount];
    private readonly string[] _dateTimeFormats = new string[4];
    private readonly Dictionary<string, Entry> _entriesBySkeleton = new(StringComparer.Ordinal);
    private string? _decimalSeparator;

    public DateTimePatternGenerator(DateTimeLocaleData data)
    {
        _data = data;
        foreach (var item in CanonicalItems)
        {
            AddPattern(item.ToString(), skeletonToUse: null, overrideExisting: false);
        }

        foreach (var style in (ReadOnlySpan<string>)["full", "long", "medium", "short"])
        {
            AddPattern(data.GetDateFormat(style), skeletonToUse: null, overrideExisting: false);
            AddPattern(data.GetTimeFormat(style), skeletonToUse: null, overrideExisting: false);
        }

        foreach (var skeleton in data.GetKeys("dateTimeFormats/availableFormats"))
        {
            // The variants (-alt-ascii) and the plural forms (-count-one) are not used
            if (skeleton.Contains('-', StringComparison.Ordinal))
                continue;

            if (data.GetString("dateTimeFormats/availableFormats/" + skeleton) is { } pattern)
            {
                AddPattern(pattern, skeleton, overrideExisting: true);
            }
        }

        for (var i = 0; i < AppendItemNames.Length; i++)
        {
            if (AppendItemNames[i] is { } name)
            {
                _appendItems[i] = data.GetString("dateTimeFormats/appendItems/" + name) ?? data.Gregorian.GetString("dateTimeFormats/appendItems/" + name);
            }
        }

        _dateTimeFormats[0] = data.GetDateTimeFormat("full");
        _dateTimeFormats[1] = data.GetDateTimeFormat("long");
        _dateTimeFormats[2] = data.GetDateTimeFormat("medium");
        _dateTimeFormats[3] = data.GetDateTimeFormat("short");

        DefaultHourSymbol = GetDefaultHourSymbol(data);
    }

    /// <summary>Gets the letter of the preferred hour cycle of the locale (h, H, K or k).</summary>
    public char DefaultHourSymbol { get; }

    private static char GetDefaultHourSymbol(DateTimeLocaleData data)
    {
        var hourCycle = DateTimeFormatLocaleData.GetDefaultHourCycle(data.Locale.Name);
        return DateTimePattern.GetHourSymbol(hourCycle);
    }

    private sealed class Entry(string basePattern, DateTimeSkeleton skeleton, string pattern, bool skeletonWasSpecified)
    {
        public string BasePattern { get; } = basePattern;

        public DateTimeSkeleton Skeleton { get; } = skeleton;

        public string Pattern { get; set; } = pattern;

        public bool SkeletonWasSpecified { get; set; } = skeletonWasSpecified;
    }

    private static int GetBucketIndex(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => 26 + c - 'a',
        _ => -1,
    };

    // addPatternWithSkeleton
    private void AddPattern(string pattern, string? skeletonToUse, bool overrideExisting)
    {
        var skeleton = DateTimeSkeleton.Create(skeletonToUse ?? pattern);
        var basePattern = skeleton.BaseOriginal;
        if (basePattern.Length == 0)
            return;

        var duplicate = FindByBasePattern(basePattern);
        if (duplicate is not null && (!duplicate.SkeletonWasSpecified || (skeletonToUse is not null && !overrideExisting)))
        {
            if (!overrideExisting)
                return;
        }

        if (_entriesBySkeleton.TryGetValue(skeleton.Original, out var sameSkeleton))
        {
            if (!overrideExisting || (skeletonToUse is not null && sameSkeleton.SkeletonWasSpecified))
                return;
        }

        var index = GetBucketIndex(basePattern[0]);
        if (index < 0)
            return;

        var bucket = _buckets[index] ??= [];
        foreach (var entry in bucket)
        {
            if (entry.BasePattern == basePattern && entry.Skeleton.HasSameTypes(skeleton))
            {
                entry.Pattern = pattern;
                entry.SkeletonWasSpecified = skeletonToUse is not null;
                return;
            }
        }

        var newEntry = new Entry(basePattern, skeleton, pattern, skeletonToUse is not null);
        bucket.Add(newEntry);
        _entriesBySkeleton.TryAdd(skeleton.Original, newEntry);
    }

    private Entry? FindByBasePattern(string basePattern)
    {
        var index = GetBucketIndex(basePattern[0]);
        if (index < 0 || _buckets[index] is not { } bucket)
            return null;

        foreach (var entry in bucket)
        {
            if (entry.BasePattern == basePattern)
                return entry;
        }

        return null;
    }


    /// <summary>Gets the best pattern for a skeleton (getBestPattern), the hour fields of the skeleton being explicit (h, H, K or k).</summary>
    public string GetBestPattern(string skeletonText)
    {
        var requested = DateTimeSkeleton.Create(skeletonText);
        var best = GetBestRaw(requested, -1, out var missingFieldMask, out var extraFieldMask, out var specified);
        if (best is null)
            return "";

        if (missingFieldMask == 0 && extraFieldMask == 0)
            return AdjustFieldTypes(requested, best, specified, fixFractionalSeconds: false);

        var neededFields = requested.FieldMask;
        var datePattern = GetBestAppending(requested, neededFields & DateMask);
        var timePattern = GetBestAppending(requested, neededFields & TimeMask);
        if (datePattern.Length == 0)
            return timePattern;

        if (timePattern.Length == 0)
            return datePattern;

        // The style of the date gives the pattern that combines the date and the time
        var monthLength = requested.GetFieldLength(DateTimeSkeletonField.Month);
        var style = 3;
        if (monthLength == 4)
        {
            style = requested.HasField(DateTimeSkeletonField.Weekday) ? 0 : 1;
        }
        else if (monthLength == 3)
        {
            style = 2;
        }

        return CombineDateTime(_dateTimeFormats[style], datePattern, timePattern);
    }

    /// <summary>Formats the pattern that combines a date ({1}) and a time ({0}).</summary>
    public static string CombineDateTime(string dateTimeFormat, string datePattern, string timePattern)
    {
        return SimpleFormat(dateTimeFormat, timePattern, datePattern, null);
    }

    // SimpleFormatter: replaces {0}, {1} and {2}; the text outside the placeholders is a pattern text (it keeps its quotes)
    private static string SimpleFormat(string format, string arg0, string arg1, string? arg2)
    {
        var result = new StringBuilder(format.Length + arg0.Length + arg1.Length);
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '{' && i + 2 < format.Length && format[i + 2] == '}' && format[i + 1] is '0' or '1' or '2')
            {
                result.Append(format[i + 1] switch
                {
                    '0' => arg0,
                    '1' => arg1,
                    _ => arg2,
                });
                i += 2;
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    // getBestRaw: the entry with the smallest distance to the requested fields of the mask
    private string? GetBestRaw(DateTimeSkeleton source, int includeMask, out int missingFieldMask, out int extraFieldMask, out DateTimeSkeleton? specifiedSkeleton)
    {
        var bestDistance = int.MaxValue;
        var bestMissingFieldMask = -1;
        string? bestPattern = null;
        missingFieldMask = 0;
        extraFieldMask = 0;
        specifiedSkeleton = null;
        foreach (var bucket in _buckets)
        {
            if (bucket is null)
                continue;

            foreach (var entry in bucket)
            {
                var distance = source.GetDistance(entry.Skeleton, includeMask, out var missing, out var extra);
                if (distance < bestDistance || (distance == bestDistance && bestMissingFieldMask < missing))
                {
                    bestDistance = distance;
                    bestMissingFieldMask = missing;
                    bestPattern = entry.Pattern;
                    specifiedSkeleton = entry.SkeletonWasSpecified ? entry.Skeleton : null;
                    missingFieldMask = missing;
                    extraFieldMask = extra;
                    if (distance == 0)
                        return bestPattern;
                }
            }
        }

        return bestPattern;
    }

    // getBestAppending: the best pattern for the fields of a mask, with the missing fields appended (appendItems)
    private string GetBestAppending(DateTimeSkeleton requested, int missingFields)
    {
        if (missingFields == 0)
            return "";

        var raw = GetBestRaw(requested, missingFields, out var missingFieldMask, out _, out var specified);
        if (raw is null)
            return "";

        var resultPattern = AdjustFieldTypes(requested, raw, specified, fixFractionalSeconds: false);
        var lastMissingFieldMask = 0;
        while (missingFieldMask != 0)
        {
            if (lastMissingFieldMask == missingFieldMask)
                break;

            if ((missingFieldMask & SecondAndFractionalMask) == FractionalMask && (missingFields & SecondAndFractionalMask) == SecondAndFractionalMask)
            {
                resultPattern = AdjustFieldTypes(requested, resultPattern, specified, fixFractionalSeconds: true);
                missingFieldMask &= ~FractionalMask;
                continue;
            }

            var startingMask = missingFieldMask;
            var tempRaw = GetBestRaw(requested, missingFieldMask, out missingFieldMask, out _, out specified);
            if (tempRaw is null)
                break;

            var tempPattern = AdjustFieldTypes(requested, tempRaw, specified, fixFractionalSeconds: false);
            var foundMask = startingMask & ~missingFieldMask;
            var topField = foundMask == 0 ? 0 : Math.Min(31 - int.LeadingZeroCount(foundMask), (int)DateTimeSkeletonField.Zone);
            if (_appendItems[topField] is { Length: > 0 } appendItem)
            {
                resultPattern = SimpleFormat(appendItem, resultPattern, tempPattern, GetAppendName(topField));
            }

            lastMissingFieldMask = missingFieldMask;
        }

        return resultPattern;
    }

    private string GetAppendName(int field)
    {
        var name = FieldDisplayNames[field] is { } key ? _data.GetLocaleString(CldrCategory.Fields, key + "/displayName") : null;
        return "'" + (name ?? "").Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    // adjustFieldTypes: the widths of the fields of a pattern adjusted to the requested ones
    private string AdjustFieldTypes(DateTimeSkeleton requested, string pattern, DateTimeSkeleton? specifiedSkeleton, bool fixFractionalSeconds)
    {
        var result = new StringBuilder(pattern.Length);
        foreach (var part in DateTimePattern.Parse(pattern))
        {
            if (part.IsLiteral)
            {
                DateTimePattern.AppendLiteral(result, part.Literal!);
                continue;
            }

            if (DateTimeSkeleton.GetType(part.Symbol, part.Count) is not { } row)
            {
                result.Append(part.Symbol, part.Count);
                continue;
            }

            var field = row.Field;
            if (fixFractionalSeconds && field is DateTimeSkeletonField.Second)
            {
                result.Append(part.Symbol, part.Count);
                result.Append(_decimalSeparator ??= _data.Locale.GetNumberSymbols(_data.Locale.GetDefaultNumberingSystem()).Decimal);
                result.Append(requested.GetFieldSymbol(DateTimeSkeletonField.FractionalSecond), requested.GetFieldLength(DateTimeSkeletonField.FractionalSecond));
                continue;
            }

            if (requested.FieldTypes[(int)field] == 0)
            {
                result.Append(part.Symbol, part.Count);
                continue;
            }

            var requestedSymbol = requested.GetFieldSymbol(field);
            var requestedLength = requested.GetFieldLength(field);
            if (requestedSymbol == 'E' && requestedLength < 3)
            {
                requestedLength = 3;
            }

            // The width of the hour is matched (UDATPG_MATCH_HOUR_FIELD_LENGTH, as V8), not the ones of the minutes and the seconds
            var adjustedLength = requestedLength;
            if (field is DateTimeSkeletonField.Minute or DateTimeSkeletonField.Second)
            {
                adjustedLength = part.Count;
            }
            else if (specifiedSkeleton is not null && requestedSymbol is not ('c' or 'e'))
            {
                var skeletonLength = specifiedSkeleton.GetFieldLength(field);
                var patternIsNumeric = row.Type > 0;
                var skeletonIsNumeric = specifiedSkeleton.FieldTypes[(int)field] > 0;
                if (skeletonLength == requestedLength || patternIsNumeric != skeletonIsNumeric)
                {
                    adjustedLength = part.Count;
                }
            }

            var symbol = field is not (DateTimeSkeletonField.Hour or DateTimeSkeletonField.Month or DateTimeSkeletonField.Weekday) && (field is not DateTimeSkeletonField.Year || requestedSymbol == 'Y')
                ? requestedSymbol
                : part.Symbol;
            if (symbol == 'E' && adjustedLength < 3)
            {
                symbol = 'e';
            }

            if (field is DateTimeSkeletonField.Hour)
            {
                var defaultSymbol = DefaultHourSymbol;
                if (requestedSymbol == defaultSymbol)
                {
                    symbol = defaultSymbol;
                }
                else if (requestedSymbol == 'h' && defaultSymbol == 'K')
                {
                    symbol = 'K';
                }
                else if (requestedSymbol == 'H' && defaultSymbol == 'k')
                {
                    symbol = 'k';
                }
                else if (requestedSymbol == 'k' && defaultSymbol == 'H')
                {
                    symbol = 'H';
                }
                else if (requestedSymbol == 'K' && defaultSymbol == 'h')
                {
                    symbol = 'h';
                }
            }

            result.Append(symbol, adjustedLength);
        }

        return result.ToString();
    }
}
