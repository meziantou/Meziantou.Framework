using System.Buffers;
using System.Buffers.Text;

namespace Meziantou.Framework.Csv.Serialization.Converters;

internal sealed class ByteArrayCsvConverter : CsvConverter<byte[]?>
{
    public override byte[] Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        var result = new byte[Base64.GetMaxDecodedFromUtf8Length(value.Length)];
        if (!Convert.TryFromBase64Chars(value, result, out var written))
            throw new FormatException("The value is not valid Base64.");

        return written == result.Length ? result : result.AsSpan(0, written).ToArray();
    }

    public override byte[] Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        var result = new byte[Base64.GetMaxDecodedFromUtf8Length(utf8Value.Length)];
        if (Base64.DecodeFromUtf8(utf8Value, result, out _, out var written) is not OperationStatus.Done)
            throw new FormatException("The value is not valid Base64.");

        return written == result.Length ? result : result.AsSpan(0, written).ToArray();
    }

    public override bool TryFormat(byte[]? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            charsWritten = 0;
            return true;
        }

        return Convert.TryToBase64Chars(value, destination, out charsWritten);
    }

    public override bool TryFormat(byte[]? value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (value is null)
        {
            bytesWritten = 0;
            return true;
        }

        return Base64.EncodeToUtf8(value, utf8Destination, out _, out bytesWritten) is OperationStatus.Done;
    }
}
