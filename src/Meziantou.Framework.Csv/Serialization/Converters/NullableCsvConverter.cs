namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class NullableCsvConverter<T>(CsvConverter<T> converter) : CsvConverter<T?>
    where T : struct
{
    public override T? Read(ReadOnlySpan<char> value, CsvConverterContext context) => converter.Read(value, context);

    public override T? Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context) => converter.Read(utf8Value, context);

    internal override bool IsInvariantNumber(CsvConverterContext context) => converter.IsInvariantNumber(context);

    public override bool TryFormat(T? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            charsWritten = 0;
            return true;
        }

        return converter.TryFormat(value.GetValueOrDefault(), destination, out charsWritten, context);
    }

    public override bool TryFormat(T? value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            bytesWritten = 0;
            return true;
        }

        return converter.TryFormat(value.GetValueOrDefault(), utf8Destination, out bytesWritten, context);
    }
}
