using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>
/// Converts <see cref="double"/>, <see cref="float"/> and <see cref="Half"/> values. A number with few digits, such as <c>-123.45</c>, read with the
/// invariant culture, is parsed faster than with <c>T.Parse</c>, with the same result.
/// </summary>
internal sealed class FloatingPointCsvConverter<T> : SpanParsableCsvConverter<T>
    where T : ISpanParsable<T>, ISpanFormattable, IUtf8SpanParsable<T>, IUtf8SpanFormattable
{
    public override T Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (InvariantNumbers.IsInvariant(context.FormatProvider) && InvariantFloatingPoint.TryParse(value, out T result))
            return result;

        return T.Parse(value, context.FormatProvider);
    }

    public override T Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        if (InvariantNumbers.IsInvariant(context.FormatProvider) && InvariantFloatingPoint.TryParse(utf8Value, out T result))
            return result;

        return T.Parse(utf8Value, context.FormatProvider);
    }

    public override bool TryFormat(T value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (typeof(T) == typeof(double) && context.Format is null && InvariantNumbers.IsInvariant(context.FormatProvider) &&
            InvariantFloatingPoint.TryFormat((double)(object)value, destination, out charsWritten, out var tooSmall))
        {
            return !tooSmall;
        }

        return value.TryFormat(destination, out charsWritten, context.Format, context.FormatProvider);
    }

    public override bool TryFormat(T value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (typeof(T) == typeof(double) && context.Format is null && InvariantNumbers.IsInvariant(context.FormatProvider) &&
            InvariantFloatingPoint.TryFormat((double)(object)value, utf8Destination, out bytesWritten, out var tooSmall))
        {
            return !tooSmall;
        }

        return value.TryFormat(utf8Destination, out bytesWritten, context.Format, context.FormatProvider);
    }
}
