namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class UriCsvConverter : CsvConverter<Uri?>
{
    public override Uri Read(ReadOnlySpan<char> value, CsvConverterContext context) => new(value.ToString(), UriKind.RelativeOrAbsolute);

    public override bool TryFormat(Uri? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        var text = value?.OriginalString ?? "";
        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }
}
