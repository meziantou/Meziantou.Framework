namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class Utf8SpanParsableCsvConverter<T> : SpanParsableCsvConverter<T>
    where T : ISpanParsable<T>, ISpanFormattable, IUtf8SpanParsable<T>, IUtf8SpanFormattable
{
    public override T Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        if (PlainNumberParser.TryParse(utf8Value, context.FormatProvider, out T result))
            return result;

        return T.Parse(utf8Value, context.FormatProvider);
    }

    public override bool TryFormat(T value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
        => value.TryFormat(utf8Destination, out bytesWritten, context.Format, context.FormatProvider);
}
