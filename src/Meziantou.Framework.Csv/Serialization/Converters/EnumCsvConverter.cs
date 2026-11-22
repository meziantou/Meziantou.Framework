namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class EnumCsvConverter<TEnum> : CsvConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
            return result;

        throw new FormatException($"'{value}' is not a valid value of {typeof(TEnum).Name}.");
    }

    public override bool TryFormat(TEnum value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => Enum.TryFormat(value, destination, out charsWritten, context.Format);
}
