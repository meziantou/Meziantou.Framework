namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class DateOnlyCsvConverter : CsvConverter<DateOnly>
{
    public override DateOnly Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (context.Format is { } format)
            return DateOnly.ParseExact(value, format, context.FormatProvider);

        // The round-trip format, written by default, has a fast path; the other formats go through the general parser
        if (Iso8601.TryParse(value, out DateOnly result))
            return result;

        return DateOnly.Parse(value, context.FormatProvider);
    }

    public override DateOnly Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        if (context.Format is null && Iso8601.TryParse(utf8Value, out DateOnly result))
            return result;

        return base.Read(utf8Value, context);
    }

    // The default format, the round-trip format (O), does not depend on the culture
    internal override bool IsInvariantNumber(CsvConverterContext context) => context.Format is null;

    public override bool TryFormat(DateOnly value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => value.TryFormat(destination, out charsWritten, context.Format ?? "O", context.FormatProvider);

    public override bool TryFormat(DateOnly value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
        => value.TryFormat(utf8Destination, out bytesWritten, context.Format ?? "O", context.FormatProvider);
}
