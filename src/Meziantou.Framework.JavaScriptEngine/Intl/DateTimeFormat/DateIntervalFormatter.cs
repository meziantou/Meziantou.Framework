namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The calendar fields compared to choose an interval pattern (the order of ICU's DateIntervalFormat).</summary>
internal enum DateIntervalField
{
    Era,
    Year,
    Month,
    Day,
    AmPm,
    Hour,
    Minute,
    Second,
    Millisecond,
    None,
}

/// <summary>
/// The interval patterns of a format (UTS 35 "intervalFormats"): for each calendar field, the pattern used when it is the
/// largest field that differs. It follows ICU's DateIntervalFormat: the best interval skeleton with the field widths
/// adjusted, the date followed by the time interval, and the fallback patterns.
/// </summary>
internal sealed class DateIntervalPatterns
{
    // An interval pattern split in two parts (the first date, the second date); a fallback pattern has no first part
    private readonly (string First, string Second)?[] _patterns = new (string, string)?[(int)DateIntervalField.None];

    private DateIntervalPatterns(DateTimeFormatRecord format)
    {
        Format = format;
    }

    public DateTimeFormatRecord Format { get; }

    /// <summary>Gets the pattern of the skeleton of the format, used by the fallback ("{0} \u2013 {1}") as ICU's DateIntervalFormat.</summary>
    public string FullPattern { get; private set; } = "";

    /// <summary>Gets the date pattern of a skeleton with a date and a time, used to format "date, time \u2013 time" (fDatePattern).</summary>
    public string? DatePattern { get; private set; }

    /// <summary>Gets the time pattern of a skeleton with a date and a time (fTimePattern).</summary>
    public string? TimePattern { get; private set; }

    /// <summary>Gets the pattern that combines a date ({1}) and a time ({0}) (fDateTimeFormat).</summary>
    public string DateTimeFormat { get; private set; } = "{1} {0}";

    /// <summary>Gets the fallback pattern ("{0} \u2013 {1}").</summary>
    public string FallbackFormat { get; private set; } = "{0} \u2013 {1}";

    public (string First, string Second)? GetPattern(DateIntervalField field) => _patterns[(int)field];

    public static DateIntervalPatterns Create(DateTimeFormatRecord format, string? hourCycle)
    {
        var result = new DateIntervalPatterns(format);
        result.Initialize(hourCycle);
        return result;
    }

    private DateTimeLocaleData Data => Format.Data;

    private string GetBestPattern(string skeleton, string? hourCycle)
    {
        var pattern = Data.Generator.GetBestPattern(skeleton);
        return DateTimePattern.GetHourCycleOfPattern(pattern) is null ? pattern : DateTimePattern.ReplaceHourCycle(pattern, hourCycle);
    }

    // initializePattern
    private void Initialize(string? hourCycle)
    {
        FallbackFormat = Data.GetString("dateTimeFormats/intervalFormats/intervalFormatFallback") ?? Data.Gregorian.GetString("dateTimeFormats/intervalFormats/intervalFormatFallback") ?? FallbackFormat;
        var skeleton = GetSkeleton(Format.Pattern);
        FullPattern = GetBestPattern(skeleton, hourCycle);
        GetDateTimeSkeleton(skeleton, out var dateSkeleton, out var normalizedDateSkeleton, out var timeSkeleton, out var normalizedTimeSkeleton);
        if (timeSkeleton.Length > 0 && dateSkeleton.Length > 0)
        {
            DateTimeFormat = Data.GetStandardDateTimeFormat("medium");
        }

        var found = SetSeparateDateTimePattern(normalizedDateSkeleton, normalizedTimeSkeleton, dateSkeleton, timeSkeleton, hourCycle);
        if (!found)
        {
            if (timeSkeleton.Length > 0 && dateSkeleton.Length == 0)
            {
                SetTimeOnlyFallbacks(timeSkeleton, hourCycle);
            }

            return;
        }

        if (timeSkeleton.Length == 0)
            return;

        if (dateSkeleton.Length == 0)
        {
            SetTimeOnlyFallbacks(timeSkeleton, hourCycle);
            return;
        }

        // A date and a time: the date and the time interval when the day is the same, else the fallback
        var fullSkeleton = skeleton;
        if (!dateSkeleton.Contains('d', StringComparison.Ordinal))
        {
            fullSkeleton = "d" + fullSkeleton;
            SetFallbackPattern(DateIntervalField.Day, fullSkeleton, hourCycle);
        }

        if (!dateSkeleton.Contains('M', StringComparison.Ordinal))
        {
            fullSkeleton = "M" + fullSkeleton;
            SetFallbackPattern(DateIntervalField.Month, fullSkeleton, hourCycle);
        }

        if (!dateSkeleton.Contains('y', StringComparison.Ordinal))
        {
            fullSkeleton = "y" + fullSkeleton;
            SetFallbackPattern(DateIntervalField.Year, fullSkeleton, hourCycle);
        }

        if (!dateSkeleton.Contains('G', StringComparison.Ordinal))
        {
            fullSkeleton = "G" + fullSkeleton;
            SetFallbackPattern(DateIntervalField.Era, fullSkeleton, hourCycle);
        }

        var datePattern = GetBestPattern(dateSkeleton, hourCycle);
        ConcatSingleDateToTimeInterval(datePattern, DateIntervalField.AmPm);
        ConcatSingleDateToTimeInterval(datePattern, DateIntervalField.Hour);
        ConcatSingleDateToTimeInterval(datePattern, DateIntervalField.Minute);
    }

    private void SetTimeOnlyFallbacks(string timeSkeleton, string? hourCycle)
    {
        // The date is added for the fallback patterns (yMd, then GyMd)
        var pattern = GetBestPattern("yMd" + timeSkeleton, hourCycle);
        _patterns[(int)DateIntervalField.Day] = ("", pattern);
        _patterns[(int)DateIntervalField.Month] = ("", pattern);
        _patterns[(int)DateIntervalField.Year] = ("", pattern);
        _patterns[(int)DateIntervalField.Era] = ("", GetBestPattern("GyMd" + timeSkeleton, hourCycle));
    }

    private void SetFallbackPattern(DateIntervalField field, string skeleton, string? hourCycle)
    {
        _patterns[(int)field] = ("", GetBestPattern(skeleton, hourCycle));
    }

    private void ConcatSingleDateToTimeInterval(string datePattern, DateIntervalField field)
    {
        if (_patterns[(int)field] is { First.Length: > 0 } timePattern)
        {
            var combined = DateTimePatternGenerator.CombineDateTime(DateTimeFormat, datePattern, timePattern.First + timePattern.Second);
            SetIntervalPattern(field, combined);
        }
    }

    // The skeleton of a pattern (staticGetSkeleton, as V8 creates the DateIntervalFormat from the pattern)
    private static string GetSkeleton(string pattern) => DateTimeSkeleton.Create(pattern).GetSkeleton();

    // getDateTimeSkeleton: the date and time parts of a skeleton, and their normalized forms
    private static void GetDateTimeSkeleton(string skeleton, out string dateSkeleton, out string normalizedDateSkeleton, out string timeSkeleton, out string normalizedTimeSkeleton)
    {
        var date = new StringBuilder();
        var normalizedDate = new StringBuilder();
        var time = new StringBuilder();
        var normalizedTime = new StringBuilder();
        int eCount = 0, dCount = 0, mCount = 0, yCount = 0, minuteCount = 0, vCount = 0, zCount = 0;
        var hourChar = '\0';
        foreach (var c in skeleton)
        {
            switch (c)
            {
                case 'E':
                    date.Append(c);
                    eCount++;
                    break;
                case 'd':
                    date.Append(c);
                    dCount++;
                    break;
                case 'M':
                    date.Append(c);
                    mCount++;
                    break;
                case 'y':
                    date.Append(c);
                    yCount++;
                    break;
                case 'G' or 'Y' or 'u' or 'Q' or 'q' or 'L' or 'l' or 'W' or 'w' or 'D' or 'F' or 'g' or 'e' or 'c' or 'U' or 'r':
                    normalizedDate.Append(c);
                    date.Append(c);
                    break;
                case 'h' or 'H' or 'k' or 'K':
                    time.Append(c);
                    if (hourChar == '\0')
                    {
                        hourChar = c;
                    }

                    break;
                case 'm':
                    time.Append(c);
                    minuteCount++;
                    break;
                case 'z':
                    zCount++;
                    time.Append(c);
                    break;
                case 'v':
                    vCount++;
                    time.Append(c);
                    break;
                case 'a' or 'V' or 'Z' or 'j' or 's' or 'S' or 'A' or 'b' or 'B':
                    time.Append(c);
                    normalizedTime.Append(c);
                    break;
            }
        }

        normalizedDate.Append('y', yCount);
        if (mCount > 0)
        {
            normalizedDate.Append('M', mCount < 3 ? 1 : Math.Min(mCount, 5));
        }

        if (eCount > 0)
        {
            normalizedDate.Append('E', eCount <= 3 ? 1 : Math.Min(eCount, 5));
        }

        if (dCount > 0)
        {
            normalizedDate.Append('d');
        }

        if (hourChar != '\0')
        {
            normalizedTime.Append(hourChar);
        }

        if (minuteCount > 0)
        {
            normalizedTime.Append('m');
        }

        if (zCount > 0)
        {
            normalizedTime.Append('z');
        }

        if (vCount > 0)
        {
            normalizedTime.Append('v');
        }

        dateSkeleton = date.ToString();
        normalizedDateSkeleton = normalizedDate.ToString();
        timeSkeleton = time.ToString();
        normalizedTimeSkeleton = normalizedTime.ToString();
    }

    // setSeparateDateTimePtn
    private bool SetSeparateDateTimePattern(string normalizedDateSkeleton, string normalizedTimeSkeleton, string dateSkeleton, string timeSkeleton, string? hourCycle)
    {
        var skeleton = normalizedTimeSkeleton.Length > 0 ? normalizedTimeSkeleton : normalizedDateSkeleton;
        var bestSkeleton = GetBestSkeleton(skeleton, out var differenceInfo);
        if (bestSkeleton is null)
            return false;

        // The patterns of the normalized skeletons, as ICU
        if (dateSkeleton.Length > 0)
        {
            DatePattern = GetBestPattern(normalizedDateSkeleton, hourCycle);
        }

        if (timeSkeleton.Length > 0)
        {
            TimePattern = GetBestPattern(normalizedTimeSkeleton, hourCycle);
        }

        if (differenceInfo == -1)
            return false;

        if (normalizedTimeSkeleton.Length == 0)
        {
            SetIntervalPattern(DateIntervalField.Day, skeleton, bestSkeleton, differenceInfo, extend: true, out _, out _);
            var extended = SetIntervalPattern(DateIntervalField.Month, skeleton, bestSkeleton, differenceInfo, extend: true, out var extendedSkeleton, out var extendedBestSkeleton);
            if (extended)
            {
                skeleton = extendedSkeleton!;
                bestSkeleton = extendedBestSkeleton!;
            }

            SetIntervalPattern(DateIntervalField.Year, skeleton, bestSkeleton, differenceInfo, extend: true, out _, out _);
            SetIntervalPattern(DateIntervalField.Era, skeleton, bestSkeleton, differenceInfo, extend: true, out _, out _);
        }
        else
        {
            SetIntervalPattern(DateIntervalField.Minute, skeleton, bestSkeleton, differenceInfo, extend: false, out _, out _);
            SetIntervalPattern(DateIntervalField.Hour, skeleton, bestSkeleton, differenceInfo, extend: false, out _, out _);
            SetIntervalPattern(DateIntervalField.AmPm, skeleton, bestSkeleton, differenceInfo, extend: false, out _, out _);
        }

        return true;
    }

    private static char GetFieldLetter(DateIntervalField field) => field switch
    {
        DateIntervalField.Era => 'G',
        DateIntervalField.Year => 'y',
        DateIntervalField.Month => 'M',
        DateIntervalField.Day => 'd',
        DateIntervalField.AmPm => 'a',
        DateIntervalField.Hour => 'h',
        DateIntervalField.Minute => 'm',
        _ => 's',
    };

    // The interval pattern of a skeleton of the data when a field differs (the letters of the data: G y M d a B h H m)
    private string? GetIntervalPattern(string skeleton, DateIntervalField field)
    {
        var basePath = "dateTimeFormats/intervalFormats/" + skeleton + "/";
        return field switch
        {
            DateIntervalField.Era => Data.GetString(basePath + "G"),
            DateIntervalField.Year => Data.GetString(basePath + "y"),
            DateIntervalField.Month => Data.GetString(basePath + "M"),
            DateIntervalField.Day => Data.GetString(basePath + "d"),
            DateIntervalField.AmPm => Data.GetString(basePath + "a") ?? Data.GetString(basePath + "B"),
            DateIntervalField.Hour => Data.GetString(basePath + "h") ?? Data.GetString(basePath + "H"),
            DateIntervalField.Minute => Data.GetString(basePath + "m"),
            _ => null,
        };
    }

    // DateIntervalFormat::setIntervalPattern(field, skeleton, bestSkeleton, differenceInfo, extendedSkeleton, extendedBestSkeleton)
    private bool SetIntervalPattern(DateIntervalField field, string skeleton, string bestSkeleton, int differenceInfo, bool extend, out string? extendedSkeleton, out string? extendedBestSkeleton)
    {
        extendedSkeleton = null;
        extendedBestSkeleton = null;
        var pattern = GetIntervalPattern(bestSkeleton, field);
        if (pattern is null)
        {
            if (IsFieldUnitIgnored(bestSkeleton, field))
                return false;

            if (field is DateIntervalField.AmPm)
            {
                pattern = GetIntervalPattern(bestSkeleton, DateIntervalField.Hour);
                if (pattern is not null)
                {
                    SetIntervalPattern(field, AdjustFieldWidth(skeleton, bestSkeleton, pattern, differenceInfo));
                }

                return false;
            }

            if (extend)
            {
                var fieldLetter = GetFieldLetter(field);
                extendedSkeleton = fieldLetter + skeleton;
                extendedBestSkeleton = fieldLetter + bestSkeleton;
                pattern = GetIntervalPattern(extendedBestSkeleton, field);
                if (pattern is null && differenceInfo == 0)
                {
                    var tmpBest = GetBestSkeleton(extendedBestSkeleton, out var tmpDifference);
                    if (tmpBest is not null && tmpDifference != -1)
                    {
                        pattern = GetIntervalPattern(tmpBest, field);
                        bestSkeleton = tmpBest;
                        differenceInfo = tmpDifference;
                    }
                }
                else if (pattern is not null)
                {
                    bestSkeleton = extendedBestSkeleton;
                }

                skeleton = extendedSkeleton;
            }
        }

        if (pattern is not null)
        {
            if (differenceInfo != 0)
            {
                pattern = AdjustFieldWidth(skeleton, bestSkeleton, pattern, differenceInfo);
            }

            SetIntervalPattern(field, pattern);
        }

        return extendedSkeleton is not null;
    }

    private void SetIntervalPattern(DateIntervalField field, string intervalPattern)
    {
        var splitPoint = SplitPatternInTwoParts(intervalPattern);
        _patterns[(int)field] = (intervalPattern[..splitPoint], intervalPattern[splitPoint..]);
    }

    // splitPatternInto2Part: the index of the first repeated pattern letter
    private static int SplitPatternInTwoParts(string intervalPattern)
    {
        var inQuote = false;
        var previous = '\0';
        var count = 0;
        var seen = new HashSet<char>();
        var foundRepetition = false;
        int i;
        for (i = 0; i < intervalPattern.Length; i++)
        {
            var c = intervalPattern[i];
            if (c != previous && count > 0)
            {
                if (!seen.Add(previous))
                {
                    foundRepetition = true;
                    break;
                }

                count = 0;
            }

            if (c == '\'')
            {
                if (i + 1 < intervalPattern.Length && intervalPattern[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    inQuote = !inQuote;
                }
            }
            else if (!inQuote && char.IsAsciiLetter(c))
            {
                previous = c;
                count++;
            }
        }

        if (count > 0 && !foundRepetition && !seen.Contains(previous))
        {
            count = 0;
        }

        return i - count;
    }

    // isFieldUnitIgnored: whether the smallest field of a pattern or skeleton is larger than a calendar field
    public static bool IsFieldUnitIgnored(string pattern, DateIntervalField field)
    {
        var fieldLevel = GetFieldLevel(field);
        foreach (var part in DateTimePattern.Parse(pattern))
        {
            if (!part.IsLiteral && fieldLevel <= GetSymbolLevel(part.Symbol))
                return false;
        }

        return true;
    }

    private static int GetFieldLevel(DateIntervalField field) => field switch
    {
        DateIntervalField.Era => 0,
        DateIntervalField.Year => 10,
        DateIntervalField.Month => 20,
        DateIntervalField.Day => 30,
        DateIntervalField.AmPm => 40,
        DateIntervalField.Hour => 50,
        DateIntervalField.Minute => 60,
        DateIntervalField.Second => 70,
        _ => 80,
    };

    private static int GetSymbolLevel(char symbol) => symbol switch
    {
        'G' => 0,
        'y' or 'Y' or 'u' or 'U' or 'r' => 10,
        'M' or 'L' or 'Q' or 'q' or 'w' or 'W' => 20,
        'd' or 'D' or 'E' or 'e' or 'c' or 'F' or 'g' => 30,
        'a' or 'b' or 'B' => 40,
        'h' or 'H' or 'k' or 'K' => 50,
        'm' => 60,
        's' => 70,
        'S' or 'A' => 80,
        'z' or 'Z' or 'v' or 'V' or 'O' or 'X' or 'x' => 65,
        _ => -1,
    };

    private static Dictionary<char, int> ParseSkeleton(string skeleton)
    {
        var result = new Dictionary<char, int>();
        foreach (var c in skeleton)
        {
            result[c] = result.TryGetValue(c, out var count) ? count + 1 : 1;
        }

        return result;
    }

    // DateIntervalInfo::getBestSkeleton: 0 = same skeleton, 1 = same fields with other widths, 2 = only z/v, h/K, H/k or a/b differ, -1 = other fields
    private string? GetBestSkeleton(string skeleton, out int bestMatchDistanceInfo)
    {
        var input = skeleton;
        var replaced = false;
        if (input.AsSpan().IndexOfAny("zkKab") >= 0)
        {
            input = input.Replace('z', 'v').Replace('k', 'H').Replace('K', 'h').Replace("a", "", StringComparison.Ordinal).Replace("b", "", StringComparison.Ordinal);
            replaced = true;
        }

        var inputWidths = ParseSkeleton(input);
        var bestDistance = int.MaxValue;
        string? bestSkeleton = null;
        bestMatchDistanceInfo = 0;
        foreach (var candidate in Data.GetKeys("dateTimeFormats/intervalFormats"))
        {
            if (candidate is "intervalFormatFallback")
                continue;

            var widths = ParseSkeleton(candidate);
            var distance = 0;
            var fieldDifference = 1;
            foreach (var letter in inputWidths.Keys.Union(widths.Keys))
            {
                inputWidths.TryGetValue(letter, out var inputWidth);
                widths.TryGetValue(letter, out var width);
                if (inputWidth == width)
                    continue;

                if (inputWidth == 0 || width == 0)
                {
                    fieldDifference = -1;
                    distance += 0x1000;
                }
                else if (letter == 'M' && ((inputWidth <= 2 && width > 2) || (inputWidth > 2 && width <= 2)))
                {
                    distance += 0x100;
                }
                else
                {
                    distance += Math.Abs(inputWidth - width);
                }
            }

            if (distance < bestDistance)
            {
                bestSkeleton = candidate;
                bestDistance = distance;
                bestMatchDistanceInfo = fieldDifference;
            }

            if (distance == 0)
            {
                bestMatchDistanceInfo = 0;
                break;
            }
        }

        if (replaced && bestMatchDistanceInfo != -1)
        {
            bestMatchDistanceInfo = 2;
        }

        return bestSkeleton;
    }

    // adjustFieldWidth: the widths of the fields of the interval pattern adjusted to the requested skeleton
    private static string AdjustFieldWidth(string inputSkeleton, string bestMatchSkeleton, string bestIntervalPattern, int differenceInfo)
    {
        var adjusted = bestIntervalPattern;
        var inputWidths = ParseSkeleton(inputSkeleton);
        var bestWidths = ParseSkeleton(bestMatchSkeleton);
        if (differenceInfo == 2)
        {
            if (inputSkeleton.Contains('z', StringComparison.Ordinal))
            {
                adjusted = ReplaceLetter(adjusted, 'v', 'z');
            }

            if (inputSkeleton.Contains('K', StringComparison.Ordinal))
            {
                adjusted = ReplaceLetter(adjusted, 'h', 'K');
            }

            if (inputSkeleton.Contains('k', StringComparison.Ordinal))
            {
                adjusted = ReplaceLetter(adjusted, 'H', 'k');
            }

            if (inputSkeleton.Contains('b', StringComparison.Ordinal))
            {
                adjusted = ReplaceLetter(adjusted, 'a', 'b');
            }
        }

        if (adjusted.Contains('a', StringComparison.Ordinal) && !bestWidths.ContainsKey('a'))
        {
            bestWidths['a'] = 1;
        }

        if (adjusted.Contains('b', StringComparison.Ordinal) && !bestWidths.ContainsKey('b'))
        {
            bestWidths['b'] = 1;
        }

        var result = new StringBuilder(adjusted.Length + 4);
        foreach (var part in DateTimePattern.Parse(adjusted))
        {
            if (part.IsLiteral)
            {
                DateTimePattern.AppendLiteral(result, part.Literal!);
                continue;
            }

            var skeletonChar = part.Symbol == 'L' ? 'M' : part.Symbol;
            bestWidths.TryGetValue(skeletonChar, out var fieldCount);
            inputWidths.TryGetValue(skeletonChar, out var inputFieldCount);
            var count = part.Count;
            if (fieldCount == count && inputFieldCount > fieldCount)
            {
                count = inputFieldCount;
            }

            result.Append(part.Symbol, count);
        }

        return result.ToString();
    }

    private static string ReplaceLetter(string pattern, char from, char to)
    {
        var result = new StringBuilder(pattern.Length);
        foreach (var part in DateTimePattern.Parse(pattern))
        {
            if (part.IsLiteral)
            {
                DateTimePattern.AppendLiteral(result, part.Literal!);
            }
            else
            {
                result.Append(part.Symbol == from ? to : part.Symbol, part.Count);
            }
        }

        return result.ToString();
    }
}

/// <summary>PartitionDateTimeRangePattern: the parts of a range of two instants (ICU's DateIntervalFormat).</summary>
internal static class DateIntervalFormatter
{
    private static readonly TemporalTimeZone PlainTimeZone = TemporalTimeZone.FromOffset(0);

    /// <summary>Formats a range: the parts with their source ("startRange", "endRange" or "shared").</summary>
    public static List<DateTimePart> Format(JsDateTimeFormatObject dtf, DateTimeFormatRecord format, Int128 x, Int128 y, bool isPlain)
    {
        var timeZone = isPlain ? PlainTimeZone : dtf.TimeZone;
        var time1 = DateTimeLocalTimeCalculator.ToLocalTime(x, dtf.Calendar, timeZone);
        var time2 = DateTimeLocalTimeCalculator.ToLocalTime(y, dtf.Calendar, timeZone);
        var field = GetLargestDifferentField(time1, time2);
        var result = new List<DateTimePart>();
        var numberOverrides = DateTimeFormatter.GetNumberOverrides(dtf, format);
        if (field is DateIntervalField.None)
        {
            DateTimeFormatter.FormatPattern(dtf, format.Parts, time1, result, numberOverrides: numberOverrides);
            return result;
        }

        var patterns = format.GetIntervalPatterns(dtf.HourCycle);
        var fromToOnSameDay = field is DateIntervalField.AmPm or DateIntervalField.Hour or DateIntervalField.Minute or DateIntervalField.Second or DateIntervalField.Millisecond;
        var raw = new List<(DateTimePart Part, char Field)>();
        var intervalPattern = patterns.GetPattern(field);
        if (intervalPattern is null)
        {
            if (DateIntervalPatterns.IsFieldUnitIgnored(format.Pattern, field))
            {
                DateTimeFormatter.FormatPattern(dtf, format.Parts, time1, result, numberOverrides: numberOverrides);
                return result;
            }

            FallbackFormat(dtf, patterns, patterns.FullPattern, time1, time2, fromToOnSameDay, raw);
        }
        else if (intervalPattern.Value.First.Length == 0)
        {
            FallbackFormat(dtf, patterns, intervalPattern.Value.Second, time1, time2, fromToOnSameDay, raw);
        }
        else
        {
            AppendFields(dtf, intervalPattern.Value.First, time1, raw);
            if (intervalPattern.Value.Second.Length > 0)
            {
                AppendFields(dtf, intervalPattern.Value.Second, time2, raw);
            }
        }

        return AssignSources(raw);
    }

    private static DateIntervalField GetLargestDifferentField(in DateTimeLocalTime time1, in DateTimeLocalTime time2)
    {
        if (time1.Era != time2.Era)
            return DateIntervalField.Era;

        if (time1.ExtendedYear != time2.ExtendedYear)
            return DateIntervalField.Year;

        if (time1.Month != time2.Month || time1.IsLeapMonth != time2.IsLeapMonth)
            return DateIntervalField.Month;

        if (time1.Day != time2.Day)
            return DateIntervalField.Day;

        if ((time1.Hour < 12) != (time2.Hour < 12))
            return DateIntervalField.AmPm;

        if (time1.Hour != time2.Hour)
            return DateIntervalField.Hour;

        if (time1.Minute != time2.Minute)
            return DateIntervalField.Minute;

        if (time1.Second != time2.Second)
            return DateIntervalField.Second;

        if (time1.Millisecond != time2.Millisecond)
            return DateIntervalField.Millisecond;

        return DateIntervalField.None;
    }

    private static void FallbackFormat(JsDateTimeFormatObject dtf, DateIntervalPatterns patterns, string pattern, DateTimeLocalTime time1, DateTimeLocalTime time2, bool fromToOnSameDay, List<(DateTimePart Part, char Field)> raw)
    {
        if (fromToOnSameDay && patterns.DatePattern is not null && patterns.TimePattern is not null)
        {
            // "{1}, {0}": the date, and the range of the times
            var format = patterns.DateTimeFormat;
            var timeIndex = format.IndexOf("{0}", StringComparison.Ordinal);
            var dateIndex = format.IndexOf("{1}", StringComparison.Ordinal);
            if (timeIndex >= 0 && dateIndex >= 0)
            {
                var first = Math.Min(timeIndex, dateIndex);
                var second = Math.Max(timeIndex, dateIndex);
                AppendLiteral(raw, UnquotePatternText(format[..first]));
                AppendPlaceholder(first == timeIndex);
                AppendLiteral(raw, UnquotePatternText(format[(first + 3)..second]));
                AppendPlaceholder(second == timeIndex);
                AppendLiteral(raw, UnquotePatternText(format[(second + 3)..]));
                return;

                void AppendPlaceholder(bool isTime)
                {
                    if (isTime)
                    {
                        FallbackRange(dtf, patterns, patterns.TimePattern!, time1, time2, raw);
                    }
                    else
                    {
                        AppendFields(dtf, patterns.DatePattern!, time1, raw);
                    }
                }
            }
        }

        FallbackRange(dtf, patterns, pattern, time1, time2, raw);
    }

    private static void FallbackRange(JsDateTimeFormatObject dtf, DateIntervalPatterns patterns, string pattern, in DateTimeLocalTime time1, in DateTimeLocalTime time2, List<(DateTimePart Part, char Field)> raw)
    {
        var fallback = patterns.FallbackFormat;
        var index0 = fallback.IndexOf("{0}", StringComparison.Ordinal);
        var index1 = fallback.IndexOf("{1}", StringComparison.Ordinal);
        if (index0 < 0 || index1 < 0)
        {
            index0 = 0;
            index1 = 3;
            fallback = "{0}{1}";
        }

        var firstIsStart = index0 < index1;
        var first = Math.Min(index0, index1);
        var second = Math.Max(index0, index1);
        AppendLiteral(raw, fallback[..first]);
        AppendFields(dtf, pattern, firstIsStart ? time1 : time2, raw);
        AppendLiteral(raw, fallback[(first + 3)..second]);
        AppendFields(dtf, pattern, firstIsStart ? time2 : time1, raw);
        AppendLiteral(raw, fallback[(second + 3)..]);
    }

    // The text of a pattern of dateTimeFormats outside the placeholders (it can be quoted, e.g. "'at'")
    private static string UnquotePatternText(string text)
    {
        if (!text.Contains('\'', StringComparison.Ordinal))
            return text;

        var result = new StringBuilder();
        foreach (var part in DateTimePattern.Parse(text))
        {
            result.Append(part.IsLiteral ? part.Literal : new string(part.Symbol, part.Count));
        }

        return result.ToString();
    }

    private static void AppendLiteral(List<(DateTimePart Part, char Field)> raw, string value)
    {
        if (value.Length > 0)
        {
            raw.Add((new DateTimePart("literal", value), '\0'));
        }
    }

    private static void AppendFields(JsDateTimeFormatObject dtf, string pattern, in DateTimeLocalTime time, List<(DateTimePart Part, char Field)> raw)
    {
        var formatted = new List<DateTimePart>();
        var symbols = new List<char>();
        DateTimeFormatter.FormatPattern(dtf, DateTimePattern.Parse(pattern), time, formatted, symbols: symbols);
        for (var i = 0; i < formatted.Count; i++)
        {
            raw.Add((formatted[i], symbols[i] == '\0' ? '\0' : GetFieldKey(symbols[i])));
        }
    }

    // The fields that are the same calendar field (e.g. M and L)
    private static char GetFieldKey(char symbol) => symbol switch
    {
        'L' => 'M',
        'c' or 'e' => 'E',
        'H' or 'k' or 'K' => 'h',
        'b' or 'B' => 'a',
        _ => symbol,
    };

    // The sources of the parts: the parts from the first to the last field repeated in the first date are the start, the
    // ones of their repetitions in the second date are the end, the others are shared (ICU's addOverlapSpans)
    private static List<DateTimePart> AssignSources(List<(DateTimePart Part, char Field)> raw)
    {
        // Merge the adjacent literals
        var merged = new List<(DateTimePart Part, char Field)>(raw.Count);
        foreach (var item in raw)
        {
            if (item.Field == '\0' && merged.Count > 0 && merged[^1].Field == '\0' && merged[^1].Part.Type is "literal" && item.Part.Type is "literal")
            {
                merged[^1] = (merged[^1].Part with { Value = merged[^1].Part.Value + item.Part.Value }, '\0');
            }
            else
            {
                merged.Add(item);
            }
        }

        var starts = new int[merged.Count];
        var position = 0;
        for (var i = 0; i < merged.Count; i++)
        {
            starts[i] = position;
            position += merged[i].Part.Value.Length;
        }

        var span1Start = int.MaxValue;
        var span1End = -1;
        var span2Start = int.MaxValue;
        var span2End = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Field == '\0')
                continue;

            for (var j = i + 1; j < merged.Count; j++)
            {
                if (merged[j].Field != merged[i].Field)
                    continue;

                span1Start = Math.Min(span1Start, starts[i]);
                span1End = Math.Max(span1End, starts[i] + merged[i].Part.Value.Length);
                span2Start = Math.Min(span2Start, starts[j]);
                span2End = Math.Max(span2End, starts[j] + merged[j].Part.Value.Length);
                break;
            }
        }

        var result = new List<DateTimePart>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            var start = starts[i];
            var end = start + merged[i].Part.Value.Length;
            var source = "shared";
            if (span1End >= 0 && start >= span1Start && end <= span1End)
            {
                source = "startRange";
            }
            else if (span2End >= 0 && start >= span2Start && end <= span2End)
            {
                source = "endRange";
            }

            result.Add(merged[i].Part with { Source = source });
        }

        return result;
    }
}
