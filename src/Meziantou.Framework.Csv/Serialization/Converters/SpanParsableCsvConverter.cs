using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Converters;

internal class SpanParsableCsvConverter<T> : CsvConverter<T>
    where T : ISpanParsable<T>, ISpanFormattable
{
    public override T Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (PlainNumberParser.TryParse(value, context.FormatProvider, out T result))
            return result;

        return T.Parse(value, context.FormatProvider);
    }

    internal override bool IsInvariantNumber(CsvConverterContext context) => InvariantNumbers.IsIntegerOrDecimal<T>() && context.Format is null && InvariantNumbers.IsInvariant(context.FormatProvider);

    public override bool TryFormat(T value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => value.TryFormat(destination, out charsWritten, context.Format, context.FormatProvider);
}
