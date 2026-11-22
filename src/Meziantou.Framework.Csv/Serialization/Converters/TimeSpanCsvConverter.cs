namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class TimeSpanCsvConverter : CsvConverter<TimeSpan>
{
    public override TimeSpan Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        return context.Format is { } format
            ? TimeSpan.ParseExact(value, format, context.FormatProvider)
            : TimeSpan.Parse(value, context.FormatProvider);
    }

    public override bool TryFormat(TimeSpan value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => value.TryFormat(destination, out charsWritten, context.Format ?? "c", context.FormatProvider);

    public override bool TryFormat(TimeSpan value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
        => value.TryFormat(utf8Destination, out bytesWritten, context.Format ?? "c", context.FormatProvider);
}
