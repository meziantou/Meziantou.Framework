using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The fields of a date skeleton, in the order of the skeletons (UDateTimePatternField of ICU).</summary>
internal enum DateTimeSkeletonField
{
    Era,
    Year,
    Quarter,
    Month,
    WeekOfYear,
    WeekOfMonth,
    Weekday,
    DayOfYear,
    DayOfWeekInMonth,
    Day,
    DayPeriod,
    Hour,
    Minute,
    Second,
    FractionalSecond,
    Zone,
}

/// <summary>A row of the table of the pattern letters used to match the skeletons (dtTypes of ICU's DateTimePatternGenerator).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DateTimeSkeletonType(char Symbol, DateTimeSkeletonField Field, int Type, int MinLength);

/// <summary>
/// A skeleton (UTS 35 "availableFormats" key or the requested fields): the representation of each field, its type
/// (used to compute the distance between two skeletons) and its base form. It is the DateTimeMatcher / PtnSkeleton of
/// ICU's DateTimePatternGenerator, which this matcher follows to produce the same patterns.
/// </summary>
internal sealed class DateTimeSkeleton
{
    public const int FieldCount = 16;

    private const int Numeric = 0x100;
    private const int Narrow = -0x101;
    private const int Shorter = -0x102;
    private const int Short = -0x103;
    private const int Long = -0x104;
    private const int Delta = 0x10;

    private static readonly DateTimeSkeletonType[] Types =
    [
        new('G', DateTimeSkeletonField.Era, Short, 1),
        new('G', DateTimeSkeletonField.Era, Long, 4),
        new('G', DateTimeSkeletonField.Era, Narrow, 5),

        new('y', DateTimeSkeletonField.Year, Numeric, 1),
        new('Y', DateTimeSkeletonField.Year, Numeric + Delta, 1),
        new('u', DateTimeSkeletonField.Year, Numeric + (2 * Delta), 1),
        new('r', DateTimeSkeletonField.Year, Numeric + (3 * Delta), 1),
        new('U', DateTimeSkeletonField.Year, Short, 1),
        new('U', DateTimeSkeletonField.Year, Long, 4),
        new('U', DateTimeSkeletonField.Year, Narrow, 5),

        new('Q', DateTimeSkeletonField.Quarter, Numeric, 1),
        new('Q', DateTimeSkeletonField.Quarter, Short, 3),
        new('Q', DateTimeSkeletonField.Quarter, Long, 4),
        new('Q', DateTimeSkeletonField.Quarter, Narrow, 5),
        new('q', DateTimeSkeletonField.Quarter, Numeric + Delta, 1),
        new('q', DateTimeSkeletonField.Quarter, Short - Delta, 3),
        new('q', DateTimeSkeletonField.Quarter, Long - Delta, 4),
        new('q', DateTimeSkeletonField.Quarter, Narrow - Delta, 5),

        new('M', DateTimeSkeletonField.Month, Numeric, 1),
        new('M', DateTimeSkeletonField.Month, Short, 3),
        new('M', DateTimeSkeletonField.Month, Long, 4),
        new('M', DateTimeSkeletonField.Month, Narrow, 5),
        new('L', DateTimeSkeletonField.Month, Numeric + Delta, 1),
        new('L', DateTimeSkeletonField.Month, Short - Delta, 3),
        new('L', DateTimeSkeletonField.Month, Long - Delta, 4),
        new('L', DateTimeSkeletonField.Month, Narrow - Delta, 5),
        new('l', DateTimeSkeletonField.Month, Numeric + Delta, 1),

        new('w', DateTimeSkeletonField.WeekOfYear, Numeric, 1),

        new('W', DateTimeSkeletonField.WeekOfMonth, Numeric, 1),

        new('E', DateTimeSkeletonField.Weekday, Short, 1),
        new('E', DateTimeSkeletonField.Weekday, Long, 4),
        new('E', DateTimeSkeletonField.Weekday, Narrow, 5),
        new('E', DateTimeSkeletonField.Weekday, Shorter, 6),
        new('c', DateTimeSkeletonField.Weekday, Numeric + (2 * Delta), 1),
        new('c', DateTimeSkeletonField.Weekday, Short - (2 * Delta), 3),
        new('c', DateTimeSkeletonField.Weekday, Long - (2 * Delta), 4),
        new('c', DateTimeSkeletonField.Weekday, Narrow - (2 * Delta), 5),
        new('c', DateTimeSkeletonField.Weekday, Shorter - (2 * Delta), 6),
        new('e', DateTimeSkeletonField.Weekday, Numeric + Delta, 1),
        new('e', DateTimeSkeletonField.Weekday, Short - Delta, 3),
        new('e', DateTimeSkeletonField.Weekday, Long - Delta, 4),
        new('e', DateTimeSkeletonField.Weekday, Narrow - Delta, 5),
        new('e', DateTimeSkeletonField.Weekday, Shorter - Delta, 6),

        new('d', DateTimeSkeletonField.Day, Numeric, 1),
        new('g', DateTimeSkeletonField.Day, Numeric + Delta, 1),

        new('D', DateTimeSkeletonField.DayOfYear, Numeric, 1),

        new('F', DateTimeSkeletonField.DayOfWeekInMonth, Numeric, 1),

        new('a', DateTimeSkeletonField.DayPeriod, Short, 1),
        new('a', DateTimeSkeletonField.DayPeriod, Long, 4),
        new('a', DateTimeSkeletonField.DayPeriod, Narrow, 5),
        new('b', DateTimeSkeletonField.DayPeriod, Short - Delta, 1),
        new('b', DateTimeSkeletonField.DayPeriod, Long - Delta, 4),
        new('b', DateTimeSkeletonField.DayPeriod, Narrow - Delta, 5),
        new('B', DateTimeSkeletonField.DayPeriod, Short - (3 * Delta), 1),
        new('B', DateTimeSkeletonField.DayPeriod, Long - (3 * Delta), 4),
        new('B', DateTimeSkeletonField.DayPeriod, Narrow - (3 * Delta), 5),

        new('H', DateTimeSkeletonField.Hour, Numeric + (10 * Delta), 1),
        new('k', DateTimeSkeletonField.Hour, Numeric + (11 * Delta), 1),
        new('h', DateTimeSkeletonField.Hour, Numeric, 1),
        new('K', DateTimeSkeletonField.Hour, Numeric + Delta, 1),
        new('J', DateTimeSkeletonField.Hour, Numeric + (5 * Delta), 1),
        new('j', DateTimeSkeletonField.Hour, Numeric + (6 * Delta), 1),
        new('C', DateTimeSkeletonField.Hour, Numeric + (7 * Delta), 1),

        new('m', DateTimeSkeletonField.Minute, Numeric, 1),

        new('s', DateTimeSkeletonField.Second, Numeric, 1),
        new('A', DateTimeSkeletonField.Second, Numeric + Delta, 1),

        new('S', DateTimeSkeletonField.FractionalSecond, Numeric, 1),

        new('v', DateTimeSkeletonField.Zone, Short - (2 * Delta), 1),
        new('v', DateTimeSkeletonField.Zone, Long - (2 * Delta), 4),
        new('z', DateTimeSkeletonField.Zone, Short, 1),
        new('z', DateTimeSkeletonField.Zone, Long, 4),
        new('Z', DateTimeSkeletonField.Zone, Narrow - Delta, 1),
        new('Z', DateTimeSkeletonField.Zone, Long - Delta, 4),
        new('Z', DateTimeSkeletonField.Zone, Short - Delta, 5),
        new('O', DateTimeSkeletonField.Zone, Short - Delta, 1),
        new('O', DateTimeSkeletonField.Zone, Long - Delta, 4),
        new('V', DateTimeSkeletonField.Zone, Short - Delta, 1),
        new('V', DateTimeSkeletonField.Zone, Long - Delta, 2),
        new('V', DateTimeSkeletonField.Zone, Long - 1 - Delta, 3),
        new('V', DateTimeSkeletonField.Zone, Long - 2 - Delta, 4),
        new('X', DateTimeSkeletonField.Zone, Narrow - Delta, 1),
        new('X', DateTimeSkeletonField.Zone, Short - Delta, 2),
        new('X', DateTimeSkeletonField.Zone, Long - Delta, 4),
        new('x', DateTimeSkeletonField.Zone, Narrow - Delta, 1),
        new('x', DateTimeSkeletonField.Zone, Short - Delta, 2),
        new('x', DateTimeSkeletonField.Zone, Long - Delta, 4),
    ];

    private readonly char[] _symbols = new char[FieldCount];
    private readonly int[] _lengths = new int[FieldCount];
    private readonly char[] _baseSymbols = new char[FieldCount];
    private readonly int[] _baseLengths = new int[FieldCount];

    /// <summary>Gets the type of each field (0 when the field is absent); the distance between two skeletons is computed from them.</summary>
    public int[] FieldTypes { get; } = new int[FieldCount];

    /// <summary>Gets whether a day period "a" was added because the skeleton has a 12-hour field without day period.</summary>
    public bool AddedDefaultDayPeriod { get; private set; }

    /// <summary>Gets the canonical skeleton (the fields in the order of <see cref="DateTimeSkeletonField"/>).</summary>
    public string Original { get; private set; } = "";

    /// <summary>Gets the base skeleton: each field in its canonical form, without its width for the numeric fields.</summary>
    public string BaseOriginal { get; private set; } = "";

    /// <summary>Gets the mask of the fields of the skeleton.</summary>
    public int FieldMask
    {
        get
        {
            var result = 0;
            for (var i = 0; i < FieldCount; i++)
            {
                if (FieldTypes[i] != 0)
                {
                    result |= 1 << i;
                }
            }

            return result;
        }
    }

    public char GetFieldSymbol(DateTimeSkeletonField field) => _symbols[(int)field];

    public int GetFieldLength(DateTimeSkeletonField field) => _lengths[(int)field];

    public int GetBaseFieldLength(DateTimeSkeletonField field) => _baseLengths[(int)field];

    public bool HasField(DateTimeSkeletonField field) => _lengths[(int)field] > 0;

    /// <summary>Gets the row of <see cref="Types"/> of a field (a letter repeated), or null when the letter is not a field (getCanonicalIndex).</summary>
    public static DateTimeSkeletonType? GetType(char symbol, int length, bool strict = false)
    {
        DateTimeSkeletonType? best = null;
        for (var i = 0; i < Types.Length; i++)
        {
            var row = Types[i];
            if (row.Symbol != symbol)
                continue;

            best = row;
            if (i + 1 < Types.Length && Types[i + 1].Symbol == symbol && Types[i + 1].MinLength <= length)
                continue;

            return row;
        }

        return strict ? null : best;
    }

    /// <summary>Creates the skeleton of a pattern or of a skeleton string (DateTimeMatcher::set).</summary>
    public static DateTimeSkeleton Create(string patternOrSkeleton)
    {
        var result = new DateTimeSkeleton();
        foreach (var part in DateTimePattern.Parse(patternOrSkeleton))
        {
            if (part.IsLiteral)
                continue;

            if (GetType(part.Symbol, part.Count) is not { } row)
                continue;

            var field = (int)row.Field;
            result._symbols[field] = part.Symbol;
            result._lengths[field] = part.Count;
            result._baseSymbols[field] = row.Symbol;
            result._baseLengths[field] = row.MinLength;
            var type = row.Type;
            if (type > 0)
            {
                type += part.Count;
            }

            result.FieldTypes[field] = type;
        }

        // A skeleton with minutes and fractional seconds, but no seconds: add the seconds (ICU #20739)
        if (result.HasField(DateTimeSkeletonField.Minute) && result.HasField(DateTimeSkeletonField.FractionalSecond) && !result.HasField(DateTimeSkeletonField.Second))
        {
            result.SetField(DateTimeSkeletonField.Second, 's', 1, 's', 1, Numeric + 1);
        }

        // The day period: a 12-hour field without day period gets "a"; a 24-hour field ignores the day period
        if (result.HasField(DateTimeSkeletonField.Hour))
        {
            var hourSymbol = result.GetFieldSymbol(DateTimeSkeletonField.Hour);
            if (hourSymbol is 'h' or 'K')
            {
                if (!result.HasField(DateTimeSkeletonField.DayPeriod))
                {
                    result.SetField(DateTimeSkeletonField.DayPeriod, 'a', 1, 'a', 1, Short);
                    result.AddedDefaultDayPeriod = true;
                }
            }
            else if (result.HasField(DateTimeSkeletonField.DayPeriod))
            {
                result.SetField(DateTimeSkeletonField.DayPeriod, '\0', 0, '\0', 0, 0);
            }
        }

        result.UpdateStrings();
        return result;
    }

    private void SetField(DateTimeSkeletonField field, char symbol, int length, char baseSymbol, int baseLength, int type)
    {
        _symbols[(int)field] = symbol;
        _lengths[(int)field] = length;
        _baseSymbols[(int)field] = baseSymbol;
        _baseLengths[(int)field] = baseLength;
        FieldTypes[(int)field] = type;
    }

    private void UpdateStrings()
    {
        var original = new StringBuilder();
        var baseOriginal = new StringBuilder();
        for (var i = 0; i < FieldCount; i++)
        {
            if (_lengths[i] > 0)
            {
                original.Append(_symbols[i], _lengths[i]);
                baseOriginal.Append(_baseSymbols[i], _baseLengths[i]);
            }
        }

        Original = original.ToString();
        BaseOriginal = baseOriginal.ToString();
    }

    /// <summary>Gets the skeleton without the day period that was added by default (getSkeleton).</summary>
    public string GetSkeleton()
    {
        if (AddedDefaultDayPeriod)
        {
            var index = Original.IndexOf('a', StringComparison.Ordinal);
            if (index >= 0)
                return Original.Remove(index, 1);
        }

        return Original;
    }

    /// <summary>The distance between this (requested) skeleton and another one, restricted to the fields of a mask (getDistance).</summary>
    public int GetDistance(DateTimeSkeleton other, int includeMask, out int missingFieldMask, out int extraFieldMask)
    {
        const int ExtraField = 0x10000;
        const int MissingField = 0x1000;
        var result = 0;
        missingFieldMask = 0;
        extraFieldMask = 0;
        for (var i = 0; i < FieldCount; i++)
        {
            var myType = (includeMask & (1 << i)) == 0 ? 0 : FieldTypes[i];
            var otherType = other.FieldTypes[i];
            if (myType == otherType)
                continue;

            if (myType == 0)
            {
                result += ExtraField;
                extraFieldMask |= 1 << i;
            }
            else if (otherType == 0)
            {
                result += MissingField;
                missingFieldMask |= 1 << i;
            }
            else
            {
                result += Math.Abs(myType - otherType);
            }
        }

        return result;
    }

    /// <summary>Gets whether two skeletons have the same field types.</summary>
    public bool HasSameTypes(DateTimeSkeleton other) => FieldTypes.AsSpan().SequenceEqual(other.FieldTypes);

    public override string ToString() => Original;
}
