using System.Buffers;
using System.Text.Unicode;

namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class StringCsvConverter : CsvConverter<string?>
{
    // An empty value is an empty string, not null
    public override bool HandleNull => true;

    public override string Read(ReadOnlySpan<char> value, CsvConverterContext context) => value.IsEmpty ? "" : new string(value);

    public override string Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context) => utf8Value.IsEmpty ? "" : Encoding.UTF8.GetString(utf8Value);

    public override bool TryFormat(string? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            charsWritten = 0;
            return true;
        }

        if (value.TryCopyTo(destination))
        {
            charsWritten = value.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    public override bool TryFormat(string? value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            bytesWritten = 0;
            return true;
        }

        // Most text is ASCII, which is converted faster than UTF-8 in general
        if (Ascii.FromUtf16(value, utf8Destination, out bytesWritten) is OperationStatus.Done)
            return true;

        return Utf8.FromUtf16(value, utf8Destination, out _, out bytesWritten, replaceInvalidSequences: true) is OperationStatus.Done;
    }
}
