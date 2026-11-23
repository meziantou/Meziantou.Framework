namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class TimeSpanCsvConverter : CsvConverter<TimeSpan>
{
    public override TimeSpan Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        return context.Format is { } format
            ? TimeSpan.ParseExact(value, format, context.FormatProvider)
            : TimeSpan.Parse(value, context.FormatProvider);
    }

    // The default format, the constant format (c), does not depend on the culture
    internal override bool IsInvariantNumber(CsvConverterContext context) => context.Format is null;

    public override bool TryFormat(TimeSpan value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => value.TryFormat(destination, out charsWritten, context.Format ?? "c", context.FormatProvider);

    public override bool TryFormat(TimeSpan value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
        => value.TryFormat(utf8Destination, out bytesWritten, context.Format ?? "c", context.FormatProvider);
}
