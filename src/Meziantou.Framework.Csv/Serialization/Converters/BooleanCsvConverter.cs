namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class BooleanCsvConverter : CsvConverter<bool>
{
    public override bool Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (bool.TryParse(value, out var result))
            return result;

        var trimmed = value.Trim();
        if (trimmed is "1")
            return true;

        if (trimmed is "0")
            return false;

        throw new FormatException($"'{value}' is not a valid Boolean value: use true, false, 1 or 0.");
    }

    public override bool Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        if (utf8Value.SequenceEqual("true"u8) || utf8Value.SequenceEqual("1"u8))
            return true;

        if (utf8Value.SequenceEqual("false"u8) || utf8Value.SequenceEqual("0"u8))
            return false;

        return base.Read(utf8Value, context);
    }

    // The converter writes true or false, whatever the format
    internal override bool IsInvariantNumber(CsvConverterContext context) => true;

    public override bool TryFormat(bool value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        var text = value ? "true" : "false";
        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    public override bool TryFormat(bool value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        var text = value ? "true"u8 : "false"u8;
        if (text.TryCopyTo(utf8Destination))
        {
            bytesWritten = text.Length;
            return true;
        }

        bytesWritten = 0;
        return false;
    }
}
