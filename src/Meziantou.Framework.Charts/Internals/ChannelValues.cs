using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.Charts;

/// <summary>
/// The values of a channel for every row of a mark, as one column: numbers, times (milliseconds since the Unix epoch,
/// UTC) or categories. Missing values are NaN or <see langword="null"/>.
/// </summary>
internal sealed class ChannelValues
{
    private static readonly string?[] NoCategories = [];
    private static readonly double[] NoNumbers = [];

    private ChannelValues(ChannelKind kind, double[] numbers, string?[] categories)
    {
        Kind = kind;
        Numbers = numbers;
        Categories = categories;
    }

    public ChannelKind Kind { get; }

    public double[] Numbers { get; }

    public string?[] Categories { get; }

    public int Count => Kind is ChannelKind.Category ? Categories.Length : Numbers.Length;

    public bool IsContinuous => Kind is not ChannelKind.Category;

    public static ChannelValues FromNumbers(double[] numbers, ChannelKind kind = ChannelKind.Number) => new(kind, numbers, NoCategories);

    public static ChannelValues FromCategories(string?[] categories) => new(ChannelKind.Category, NoNumbers, categories);

    public static ChannelValues FromObjects(IReadOnlyList<object?> values)
    {
        var kind = (ChannelKind?)null;
        foreach (var value in values)
        {
            var valueKind = GetKind(value);
            if (valueKind is null)
                continue;

            if (kind is null)
            {
                kind = valueKind;
            }
            else if (kind != valueKind)
            {
                kind = ChannelKind.Category;
                break;
            }
        }

        switch (kind ?? ChannelKind.Number)
        {
            case ChannelKind.Category:
                {
                    var categories = new string?[values.Count];
                    for (var i = 0; i < categories.Length; i++)
                    {
                        categories[i] = ToCategory(values[i]);
                    }

                    return FromCategories(categories);
                }

            case ChannelKind.Time:
                {
                    var numbers = new double[values.Count];
                    for (var i = 0; i < numbers.Length; i++)
                    {
                        numbers[i] = ToTime(values[i]);
                    }

                    return FromNumbers(numbers, ChannelKind.Time);
                }

            default:
                {
                    var numbers = new double[values.Count];
                    for (var i = 0; i < numbers.Length; i++)
                    {
                        numbers[i] = ToNumber(values[i]);
                    }

                    return FromNumbers(numbers);
                }
        }
    }

    public bool IsMissing(int index) => Kind is ChannelKind.Category ? Categories[index] is null : double.IsNaN(Numbers[index]);

    public double GetNumber(int index) => Kind is ChannelKind.Category ? double.NaN : Numbers[index];

    public string? GetCategory(int index)
    {
        return Kind switch
        {
            ChannelKind.Category => Categories[index],
            ChannelKind.Time => double.IsNaN(Numbers[index]) ? null : TimeFormats.FormatCategory(Numbers[index]),
            _ => double.IsNaN(Numbers[index]) ? null : FormatNumberCategory(Numbers[index]),
        };
    }

    /// <summary>The values as categories: numbers used to group rows, such as years, are names, not quantities.</summary>
    public ChannelValues ToCategorical()
    {
        if (Kind is ChannelKind.Category)
            return this;

        var categories = new string?[Count];
        for (var i = 0; i < categories.Length; i++)
        {
            categories[i] = GetCategory(i);
        }

        return FromCategories(categories);
    }

    public ChannelValues Select(IReadOnlyList<int> indices)
    {
        if (Kind is ChannelKind.Category)
        {
            var categories = new string?[indices.Count];
            for (var i = 0; i < categories.Length; i++)
            {
                categories[i] = Categories[indices[i]];
            }

            return FromCategories(categories);
        }

        var numbers = new double[indices.Count];
        for (var i = 0; i < numbers.Length; i++)
        {
            numbers[i] = Numbers[indices[i]];
        }

        return FromNumbers(numbers, Kind);
    }

    /// <summary>A number used as a category, such as a year: the shortest text that round-trips, without thousands separators.</summary>
    internal static string FormatNumberCategory(double value) => value.ToString(CultureInfo.InvariantCulture);

    internal static ChannelKind? GetKind(object? value)
    {
        return value switch
        {
            null => null,
            double d when double.IsNaN(d) => null,
            float f when float.IsNaN(f) => null,
            double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort or Half or Int128 or UInt128 or BigInteger => ChannelKind.Number,
            DateTime or DateTimeOffset or DateOnly => ChannelKind.Time,
            _ => ChannelKind.Category,
        };
    }

    internal static double ToNumber(object? value)
    {
        return value switch
        {
            null => double.NaN,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            sbyte sb => sb,
            uint ui => ui,
            ulong ul => ul,
            ushort us => us,
            Half h => (double)h,
            Int128 i128 => (double)i128,
            UInt128 u128 => (double)u128,
            BigInteger big => (double)big,
            _ => double.NaN,
        };
    }

    internal static double ToTime(object? value)
    {
        return value switch
        {
            DateTimeOffset offset => offset.ToUnixTimeMilliseconds() + (offset.Ticks % TimeSpan.TicksPerMillisecond / (double)TimeSpan.TicksPerMillisecond),
            DateTime dateTime => TimeFormats.ToMilliseconds(dateTime.Kind is DateTimeKind.Local ? dateTime.ToUniversalTime() : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            DateOnly date => TimeFormats.ToMilliseconds(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            _ => double.NaN,
        };
    }

    internal static string? ToCategory(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            double d when double.IsNaN(d) => null,
            float f when float.IsNaN(f) => null,
            double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort or Half or Int128 or UInt128 or BigInteger => FormatNumberCategory(ToNumber(value)),
            DateTime or DateTimeOffset or DateOnly => TimeFormats.FormatCategory(ToTime(value)),
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
