namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class VersionCsvConverter : CsvConverter<Version?>
{
    public override Version Read(ReadOnlySpan<char> value, CsvConverterContext context) => Version.Parse(value);

    public override bool TryFormat(Version? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            charsWritten = 0;
            return true;
        }

        return value.TryFormat(destination, out charsWritten);
    }

    public override bool TryFormat(Version? value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            bytesWritten = 0;
            return true;
        }

        return ((IUtf8SpanFormattable)value).TryFormat(utf8Destination, out bytesWritten, default, provider: null);
    }
}
