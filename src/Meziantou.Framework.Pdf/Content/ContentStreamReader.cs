using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Content;

/// <summary>
/// Reads the operations of a content stream (ISO 32000-2, 7.8.2): operands followed by an operator. An inline image
/// (8.9.7) is returned as the operator <c>BI</c> with its dictionary and its data (a string) as operands.
/// </summary>
internal sealed class ContentStreamReader
{
    private const int MaxOperands = 4096;

    private static readonly Dictionary<string, string> Operators = CreateOperators();
    private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> OperatorLookup = Operators.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly PdfLexer _lexer;
    private readonly PdfObjectParser _parser;

    public ContentStreamReader(ReadOnlyMemory<byte> data, int maxDepth = PdfReadOptions.DefaultMaxNestingDepth)
    {
        _lexer = new PdfLexer(data);
        _parser = new PdfObjectParser(_lexer, objects: null, maxDepth);
    }

    /// <summary>Reads the next operation.</summary>
    /// <param name="operands">The list that receives the operands; it is cleared first.</param>
    /// <param name="name">The operator.</param>
    /// <returns><see langword="false"/> at the end of the content.</returns>
    public bool TryRead(List<PdfObject> operands, out string name)
    {
        operands.Clear();
        while (true)
        {
            var token = _lexer.Next();
            switch (token.Kind)
            {
                case PdfTokenKind.EndOfInput:
                    name = "";
                    return false;
                case PdfTokenKind.Keyword:
                    var bytes = _lexer.GetBytes(token);
                    name = GetOperatorName(bytes);
                    if (name == "BI")
                    {
                        ReadInlineImage(operands);
                    }
                    else if (name is "true" or "false" or "null")
                    {
                        operands.Add(name switch { "true" => PdfBoolean.True, "false" => PdfBoolean.False, _ => PdfNull.Instance });
                        continue;
                    }

                    return true;
                case PdfTokenKind.ArrayEnd or PdfTokenKind.DictionaryEnd or PdfTokenKind.Invalid or PdfTokenKind.ProcedureStart or PdfTokenKind.ProcedureEnd:
                    // A delimiter alone: skipped
                    continue;
            }

            PdfObject? value;
            try
            {
                value = _parser.ParseValue(token, depth: 0);
            }
            catch (InvalidPdfContentException)
            {
                // An array or a dictionary that is not closed at the end of the content
                name = "";
                return false;
            }

            if (value is not null && operands.Count < MaxOperands)
            {
                operands.Add(value);
            }
        }
    }

    private void ReadInlineImage(List<PdfObject> operands)
    {
        // BI key value ... ID <data> EI
        var dictionary = new PdfDictionary();
        while (true)
        {
            var token = _lexer.Next();
            if (token.Kind == PdfTokenKind.EndOfInput)
                break;

            if (token.Kind == PdfTokenKind.Keyword && _lexer.GetBytes(token).SequenceEqual("ID"u8))
                break;

            if (token.Kind != PdfTokenKind.Name)
                continue;

            var key = PdfName.FromBytes(_lexer.DecodeName(token));
            var value = _parser.ParseValue(_lexer.Next(), depth: 0);
            if (value is not null)
            {
                dictionary.SetParsed(key, value);
            }
        }

        // A single whitespace character follows ID; the data ends at /L bytes (PDF 2.0), or before the whitespace before "EI"
        var span = _lexer.Span;
        var start = Math.Min(_lexer.Position + 1, span.Length);
        var (dataEnd, endOfImage) = FindEndOfInlineImage(span, start, dictionary);
        operands.Add(dictionary);
        operands.Add(PdfString.FromBytes(span[start..dataEnd], hexadecimal: false));
        _lexer.Position = Math.Min(endOfImage + 2, span.Length);
    }

    /// <summary>Finds the end of the data of an inline image and the position of its "EI" operator.</summary>
    private static (int DataEnd, int EndOfImage) FindEndOfInlineImage(ReadOnlySpan<byte> span, int start, PdfDictionary dictionary)
    {
        var length = dictionary.GetInteger(PdfNames.L, -1);
        if (length < 0)
        {
            length = dictionary.GetInteger(PdfNames.Length, -1);
        }

        if (length >= 0 && start + length <= span.Length)
        {
            var dataEnd = (int)(start + length);
            var check = dataEnd;
            while (check < span.Length && PdfLexer.IsWhitespace(span[check]))
            {
                check++;
            }

            if (span[check..].StartsWith("EI"u8))
                return (dataEnd, check);
        }

        var position = start;
        while (position < span.Length)
        {
            var index = span[position..].IndexOf("EI"u8);
            if (index < 0)
                break;

            var found = position + index;
            var after = found + 2;
            if (found > start && PdfLexer.IsWhitespace(span[found - 1]) && (after >= span.Length || PdfLexer.IsWhitespace(span[after])) && LooksLikeContent(span[after..]))
                return (found - 1, found);

            position = found + 1;
        }

        return (span.Length, span.Length);
    }

    /// <summary>Indicates whether bytes look like the operators of a content stream: binary data would not.</summary>
    private static bool LooksLikeContent(ReadOnlySpan<byte> span)
    {
        var length = Math.Min(span.Length, 32);
        for (var i = 0; i < length; i++)
        {
            var c = span[i];
            if (c is (< 0x20 or > 0x7E) && !PdfLexer.IsWhitespace(c))
                return false;
        }

        return true;
    }

    private static string GetOperatorName(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 8)
        {
            Span<char> chars = stackalloc char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            if (OperatorLookup.TryGetValue(chars, out var known))
                return known;
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static Dictionary<string, string> CreateOperators()
    {
        string[] names =
        [
            "b", "B", "b*", "B*", "BDC", "BI", "BMC", "BT", "BX", "c", "cm", "CS", "cs", "d", "d0", "d1", "Do", "DP", "EI", "EMC", "ET", "EX", "f", "F", "f*", "G", "g", "gs", "h", "i", "ID", "j", "J", "K", "k", "l", "m", "M", "MP", "n", "q", "Q",
            "re", "RG", "rg", "ri", "s", "S", "SC", "sc", "SCN", "scn", "sh", "T*", "Tc", "Td", "TD", "Tf", "Tj", "TJ", "TL", "Tm", "Tr", "Ts", "Tw", "Tz", "v", "w", "W", "W*", "y", "'", "\"", "true", "false", "null",
        ];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            result[name] = name;
        }

        return result;
    }
}
