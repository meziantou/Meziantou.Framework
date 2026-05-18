using System.Diagnostics;
using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Provides extension methods for scrubbing values during serialization with <see cref="HumanReadableSerializerOptions"/>.
/// </summary>
public static class HumanReadableSerializerScrubExtensions
{
    /// <summary>Scrubs GUID values by replacing them with deterministic values based on their order of appearance.</summary>
    public static void ScrubGuid(this HumanReadableSerializerOptions options) => ScrubValue<Guid>(options, (value, index) =>
    {
        if (value == Guid.Empty)
            return "00000000-0000-0000-0000-000000000000";

        index += 1; // Distinct from Guid.Empty

        const string Prefix = "00000000-0000-0000-0000-";
        Span<char> data = stackalloc char[36];
        Prefix.AsSpan().CopyTo(data);
        _ = index.TryFormat(data[Prefix.Length..], out _, "000000000000", CultureInfo.InvariantCulture);
        return data.ToString();
    });

    /// <summary>Scrubs values of type T by replacing them with deterministic values like "TypeName_0", "TypeName_1", etc.</summary>
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options) => ScrubValue<T>(options, comparer: null);

    /// <summary>Scrubs values of type T by replacing them with deterministic values, using the specified comparer to determine uniqueness.</summary>
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, IEqualityComparer<T>? comparer) => ScrubValue(options, (value, index) => typeof(T).Name + "_" + index.ToString(CultureInfo.InvariantCulture), comparer);

    /// <summary>Scrubs values of type T by replacing them with the result of the specified scrubber function.</summary>
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, string> scrubber) => options.Converters.Add(new ValueScrubberConverter<T>(scrubber));

    /// <summary>Scrubs values of type T by replacing them with the result of the specified scrubber function that receives the value and its index.</summary>
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber) => ScrubValue(options, scrubber, comparer: null);

    /// <summary>Scrubs values of type T by replacing them with the result of the specified scrubber function, using the specified comparer to determine uniqueness.</summary>
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber, IEqualityComparer<T>? comparer)
        => options.Converters.Add(new ScrubValueIncrementalConverter<T>(scrubber, comparer ?? EqualityComparer<T>.Default));

    /// <summary>Serializes TimeSpan values relative to the specified origin.</summary>
    public static void UseRelativeTimeSpan(this HumanReadableSerializerOptions options, TimeSpan origin) => options.Converters.Add(new RelativeTimeSpanConverter(origin));

    /// <summary>Serializes DateTime values relative to the specified origin.</summary>
    public static void UseRelativeDateTime(this HumanReadableSerializerOptions options, DateTime origin) => options.Converters.Add(new RelativeDateTimeConverter(origin));

    /// <summary>Serializes DateTimeOffset values relative to the specified origin.</summary>
    public static void UseRelativeDateTimeOffset(this HumanReadableSerializerOptions options, DateTimeOffset origin) => options.Converters.Add(new RelativeDateTimeOffsetConverter(origin));

    private sealed class ValueScrubberConverter<T>(Func<T, string> scrubber) : HumanReadableConverter<T>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteValue(scrubber(value));
        }
    }

    private sealed class RelativeTimeSpanConverter(TimeSpan origin) : HumanReadableConverter<TimeSpan>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, TimeSpan value, HumanReadableSerializerOptions options)
        {
            WriteValueCore(writer, value - origin);
        }

        internal static void WriteValueCore(HumanReadableTextWriter writer, TimeSpan value)
        {
            writer.WriteValue(value.ToString(format: null, CultureInfo.InvariantCulture));
        }
    }

    private sealed class RelativeDateTimeConverter(DateTime origin) : HumanReadableConverter<DateTime>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, DateTime value, HumanReadableSerializerOptions options)
        {
            var diff = value - origin;
            RelativeTimeSpanConverter.WriteValueCore(writer, diff);
        }
    }

    private sealed class RelativeDateTimeOffsetConverter(DateTimeOffset origin) : HumanReadableConverter<DateTimeOffset>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, DateTimeOffset value, HumanReadableSerializerOptions options)
        {
            var diff = value - origin;
            RelativeTimeSpanConverter.WriteValueCore(writer, diff);
        }
    }

    private sealed class ScrubValueIncrementalConverter<T>(Func<T, int, string> formatValue, IEqualityComparer<T> comparer) : HumanReadableConverter<T>
    {
        private readonly string _uniqueName = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

#pragma warning disable CS8714 // Nullability of type argument doesn't match 'notnull' constraint.
            var dictionary = options.GetOrSetSerializationData(_uniqueName, () => new Dictionary<T, string>(comparer));
#pragma warning restore CS8714
            Debug.Assert(value is not null);
            if (!dictionary.TryGetValue(value, out var scrubbedValue))
            {
                scrubbedValue = formatValue(value, dictionary.Count);
                dictionary.Add(value, scrubbedValue);
            }

            writer.WriteValue(scrubbedValue);
        }
    }
}
