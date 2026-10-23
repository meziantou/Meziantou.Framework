namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Finds the objects (<c>12 0 obj</c>) and the trailers of a file by scanning its bytes, for files whose
/// cross-reference information is missing or wrong. The last definition of an object wins, as in an incremental update.
/// </summary>
internal sealed class PdfRecoveryScanner
{
    private PdfRecoveryScanner()
    {
    }

    public Dictionary<int, (long Offset, int Generation)> Offsets { get; } = [];

    /// <summary>Gets the offsets that follow the <c>trailer</c> keywords, in the order of the file.</summary>
    public List<int> TrailerOffsets { get; } = [];

    public static PdfRecoveryScanner Scan(ReadOnlyMemory<byte> data)
    {
        var scanner = new PdfRecoveryScanner();
        var span = data.Span;
        var position = 0;
        while (position < span.Length)
        {
            var index = span[position..].IndexOf("obj"u8);
            if (index < 0)
                break;

            var found = position + index;
            position = found + 3;
            if (position < span.Length && PdfLexer.IsRegular(span[position]))
                continue;

            if (TryReadHeader(span, found, out var number, out var generation, out var start))
            {
                scanner.Offsets[number] = (start, generation);
            }
        }

        position = 0;
        while (position < span.Length)
        {
            var index = span[position..].IndexOf("trailer"u8);
            if (index < 0)
                break;

            var found = position + index;
            position = found + 7;
            if ((found == 0 || !PdfLexer.IsRegular(span[found - 1])) && (position >= span.Length || !PdfLexer.IsRegular(span[position])))
            {
                scanner.TrailerOffsets.Add(position);
            }
        }

        return scanner;
    }

    /// <summary>Reads <c>number generation</c> backwards from the <c>obj</c> keyword at <paramref name="objIndex"/>.</summary>
    private static bool TryReadHeader(ReadOnlySpan<byte> span, int objIndex, out int number, out int generation, out int start)
    {
        number = 0;
        generation = 0;
        start = 0;
        var position = objIndex - 1;
        if (position < 0 || !PdfLexer.IsWhitespace(span[position]))
            return false;

        while (position >= 0 && PdfLexer.IsWhitespace(span[position]))
        {
            position--;
        }

        if (!TryReadDigitsBackwards(span, ref position, out var generationValue))
            return false;

        if (position < 0 || !PdfLexer.IsWhitespace(span[position]))
            return false;

        while (position >= 0 && PdfLexer.IsWhitespace(span[position]))
        {
            position--;
        }

        if (!TryReadDigitsBackwards(span, ref position, out var numberValue))
            return false;

        // The number starts a token
        if (position >= 0 && PdfLexer.IsRegular(span[position]))
            return false;

        if (numberValue is <= 0 or > int.MaxValue || generationValue > ushort.MaxValue)
            return false;

        number = (int)numberValue;
        generation = (int)generationValue;
        start = position + 1;
        return true;
    }

    private static bool TryReadDigitsBackwards(ReadOnlySpan<byte> span, ref int position, out long value)
    {
        value = 0;
        var end = position;
        while (position >= 0 && span[position] is >= (byte)'0' and <= (byte)'9' && end - position < 10)
        {
            position--;
        }

        if (position == end)
            return false;

        for (var i = position + 1; i <= end; i++)
        {
            value = (value * 10) + (span[i] - '0');
        }

        return true;
    }
}
