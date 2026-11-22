namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class CharCsvConverter : CsvConverter<char>
{
    public override char Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (value.Length != 1)
            throw new FormatException($"'{value}' is not a single character.");

        return value[0];
    }

    public override bool TryFormat(char value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (destination.IsEmpty)
        {
            charsWritten = 0;
            return false;
        }

        destination[0] = value;
        charsWritten = 1;
        return true;
    }
}
