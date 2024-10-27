using System.Globalization;
using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class HumanReadableSerializerScrubExtensions
{
    public static void ScrubGuid(this HumanReadableSerializerOptions options) => ScrubValue<Guid>(options, (value, index) =>
    {
        if (value == Guid.Empty)
            return "00000000-0000-0000-0000-000000000000";

        index += 1; // Distinct from Guid.Empty

        const string Prefix = "00000000-0000-0000-0000-";
#if NET6_0_OR_GREATER
        Span<char> data = stackalloc char[36];
        Prefix.AsSpan().CopyTo(data);
        _ = index.TryFormat(data[Prefix.Length..], out _, "000000000000", CultureInfo.InvariantCulture);
        return data.ToString();
#else
        return Prefix + index.ToString("000000000000", CultureInfo.InvariantCulture);
#endif
    });

    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, string> scrubber) => options.Converters.Add(new ValueScrubberConverter<T>(scrubber));
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber) => ScrubValue<T>(options, scrubber, comparer: null);
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber, IEqualityComparer<T>? comparer)
        => options.Converters.Add(new ScrubValueIncrementalConverter<T>((value, index) => scrubber(value, index), comparer ?? EqualityComparer<T>.Default));

    public static void UseRelativeTimeSpan(this HumanReadableSerializerOptions options, TimeSpan origin) => options.Converters.Add(new RelativeTimeSpanConverter(origin));
    public static void UseRelativeDateTime(this HumanReadableSerializerOptions options, DateTime origin) => options.Converters.Add(new RelativeDateTimeConverter(origin));
    public static void UseRelativeDateTimeOffset(this HumanReadableSerializerOptions options, DateTimeOffset origin) => options.Converters.Add(new RelativeDateTimeOffsetConverter(origin));

    private sealed class ValueScrubberConverter<T>(Func<T, string> scrubber) : HumanReadableConverter<T>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, T value, HumanReadableSerializerOptions options)
        {
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
        private readonly string _uniqueName = Guid.NewGuid().ToString();

        protected override void WriteValue(HumanReadableTextWriter writer, T value, HumanReadableSerializerOptions options)
        {
            var dict = options.GetOrSetSerializationData(_uniqueName, () => new Dictionary<T, string>(comparer));
            if (!dict.TryGetValue(value, out var scrubbedValue))
            {
                scrubbedValue = formatValue(value, dict.Count);
                dict.Add(value, scrubbedValue);
            }

            writer.WriteValue(scrubbedValue);
        }
    }
}
