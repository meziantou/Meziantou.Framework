namespace Meziantou.Framework.Csv.Serialization.Converters;

internal class SpanParsableCsvConverter<T> : CsvConverter<T>
    where T : ISpanParsable<T>, ISpanFormattable
{
    public override T Read(ReadOnlySpan<char> value, CsvConverterContext context) => T.Parse(value, context.FormatProvider);

    public override bool TryFormat(T value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => value.TryFormat(destination, out charsWritten, context.Format, context.FormatProvider);
}
