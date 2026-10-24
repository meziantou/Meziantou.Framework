using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Parses objects (ISO 32000-2, 7.3) and indirect objects (7.3.10) from tokens.</summary>
internal sealed class PdfObjectParser
{
    private readonly PdfIndirectObjectCollection? _objects;
    private readonly int _maxDepth;

    public PdfObjectParser(PdfLexer lexer, PdfIndirectObjectCollection? objects, int maxDepth)
    {
        Lexer = lexer;
        _objects = objects;
        _maxDepth = maxDepth;
    }

    public PdfLexer Lexer { get; }

    /// <summary>Gets or sets the limits given to the streams the parser reads.</summary>
    public PdfDecodeLimits DecodeLimits { get; set; } = PdfDecodeLimits.Default;

    /// <summary>Parses the next object.</summary>
    /// <exception cref="InvalidPdfContentException">There is no object at the position.</exception>
    public PdfObject ParseObject()
    {
        var token = Lexer.Next();
        return ParseValue(token, depth: 0) ?? throw new InvalidPdfContentException($"An object is expected at offset {token.Start}");
    }

    /// <summary>Parses an object, or returns <see langword="null"/> for a token that is no object (a keyword that ends a container, a delimiter alone).</summary>
    public PdfObject? ParseValue(PdfToken token, int depth)
    {
        if (depth > _maxDepth)
            throw new PdfResourceLimitException($"The objects are nested more than {_maxDepth} levels deep ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxNestingDepth)})");

        switch (token.Kind)
        {
            case PdfTokenKind.Integer:
                if (_objects is not null && TryParseReference(token, out var reference))
                    return reference;

                return PdfInteger.Get(token.IntegerValue);
            case PdfTokenKind.Real:
                return PdfReal.Get(token.RealValue);
            case PdfTokenKind.Name:
                return PdfName.FromBytes(Lexer.DecodeName(token));
            case PdfTokenKind.LiteralString:
                return new PdfString(Lexer.DecodeLiteralString(token), isHexadecimal: false);
            case PdfTokenKind.HexString:
                return new PdfString(Lexer.DecodeHexString(token), isHexadecimal: true);
            case PdfTokenKind.ArrayStart:
                return ParseArray(depth);
            case PdfTokenKind.DictionaryStart:
                return ParseDictionary(depth);
            case PdfTokenKind.Keyword:
                var bytes = Lexer.GetBytes(token);
                if (bytes.SequenceEqual("true"u8))
                    return PdfBoolean.True;

                if (bytes.SequenceEqual("false"u8))
                    return PdfBoolean.False;

                if (bytes.SequenceEqual("null"u8))
                    return PdfNull.Instance;

                if (IsStructureKeyword(bytes))
                    return null;

                // An unknown keyword where an object is expected: readers read it as null
                return PdfNull.Instance;
            default:
                return null;
        }
    }

    /// <summary>
    /// Parses the indirect object at the position: <c>12 0 obj ... endobj</c>, and the data of a stream.
    /// </summary>
    /// <param name="objectNumber">The object number read.</param>
    /// <param name="generation">The generation read.</param>
    /// <returns>The object, or <see langword="null"/> when there is no object header at the position.</returns>
    public PdfObject? ParseIndirectObject(out int objectNumber, out int generation)
    {
        objectNumber = 0;
        generation = 0;
        var numberToken = Lexer.Next();
        if (numberToken.Kind != PdfTokenKind.Integer || numberToken.IntegerValue is < 0 or > int.MaxValue)
            return null;

        var generationToken = Lexer.Next();
        if (generationToken.Kind != PdfTokenKind.Integer || generationToken.IntegerValue is < 0 or > int.MaxValue)
            return null;

        var objToken = Lexer.Next();
        if (!Lexer.IsKeyword(objToken, "obj"u8))
            return null;

        objectNumber = (int)numberToken.IntegerValue;
        generation = (int)generationToken.IntegerValue;

        var valueToken = Lexer.Next();
        var value = ParseValue(valueToken, depth: 0);
        if (value is null)
        {
            // "12 0 obj endobj": an empty object is null
            Lexer.Position = valueToken.Start;
            value = PdfNull.Instance;
        }

        var position = Lexer.Position;
        var next = Lexer.Next();
        if (value is PdfDictionary dictionary && Lexer.IsKeyword(next, "stream"u8))
        {
            var stream = new PdfStream(dictionary) { DecodeLimits = DecodeLimits };
            stream.SetLoadedData(ReadStreamData(dictionary));
            value = stream;
            position = Lexer.Position;
            next = Lexer.Next();
        }

        if (!Lexer.IsKeyword(next, "endobj"u8))
        {
            Lexer.Position = position;
        }

        return value;
    }

    private ReadOnlyMemory<byte> ReadStreamData(PdfDictionary dictionary)
    {
        var span = Lexer.Span;
        var start = Lexer.Position;

        // The keyword is followed by CRLF or LF (7.3.8.1); a CR alone and spaces before the end of line are accepted
        while (start < span.Length && span[start] is (byte)' ' or (byte)'\t')
        {
            start++;
        }

        if (start < span.Length && span[start] == '\r')
        {
            start++;
        }

        if (start < span.Length && span[start] == '\n')
        {
            start++;
        }

        var declaredLength = dictionary.GetInteger(PdfNames.Length, -1);
        if (declaredLength >= 0 && declaredLength <= span.Length - start)
        {
            var end = start + (int)declaredLength;
            var check = end;
            while (check < span.Length && PdfLexer.IsWhitespace(span[check]))
            {
                check++;
            }

            if (span[check..].StartsWith("endstream"u8))
            {
                Lexer.Position = check + "endstream".Length;
                return Lexer.Data[start..end];
            }
        }

        // The length is missing or wrong: the data ends at the "endstream" keyword, without the end of line before it
        var endstream = span[start..].IndexOf("endstream"u8);
        int dataEnd;
        if (endstream >= 0)
        {
            dataEnd = start + endstream;
            Lexer.Position = dataEnd + "endstream".Length;
        }
        else
        {
            var endobj = span[start..].IndexOf("endobj"u8);
            dataEnd = endobj >= 0 ? start + endobj : span.Length;
            Lexer.Position = dataEnd;
        }

        if (dataEnd > start && span[dataEnd - 1] == '\n')
        {
            dataEnd--;
        }

        if (dataEnd > start && span[dataEnd - 1] == '\r')
        {
            dataEnd--;
        }

        return Lexer.Data[start..dataEnd];
    }

    private bool TryParseReference(PdfToken numberToken, out PdfReference? reference)
    {
        reference = null;
        if (numberToken.IntegerValue is < 0 or > int.MaxValue)
            return false;

        var position = Lexer.Position;
        var generationToken = Lexer.Next();
        if (generationToken.Kind == PdfTokenKind.Integer && generationToken.IntegerValue is >= 0 and <= int.MaxValue)
        {
            var rToken = Lexer.Next();
            if (Lexer.IsKeyword(rToken, "R"u8))
            {
                reference = _objects!.GetOrCreateReference((int)numberToken.IntegerValue, (int)generationToken.IntegerValue);
                return true;
            }
        }

        Lexer.Position = position;
        return false;
    }

    private PdfArray ParseArray(int depth)
    {
        var items = new List<PdfObject>();
        while (true)
        {
            var token = Lexer.Next();
            switch (token.Kind)
            {
                case PdfTokenKind.ArrayEnd:
                    return PdfArray.Wrap(items);
                case PdfTokenKind.EndOfInput:
                    throw new InvalidPdfContentException($"The array that starts before offset {token.Start} is not closed");
                case PdfTokenKind.DictionaryEnd:
                case PdfTokenKind.Invalid:
                case PdfTokenKind.ProcedureStart:
                case PdfTokenKind.ProcedureEnd:
                    continue;
            }

            var value = ParseValue(token, depth + 1);
            if (value is null)
            {
                // "endobj" or "stream" in an array: the array is not closed
                Lexer.Position = token.Start;
                return PdfArray.Wrap(items);
            }

            items.Add(value);
        }
    }

    private PdfDictionary ParseDictionary(int depth)
    {
        var dictionary = new PdfDictionary();
        while (true)
        {
            var token = Lexer.Next();
            switch (token.Kind)
            {
                case PdfTokenKind.DictionaryEnd:
                    return dictionary;
                case PdfTokenKind.EndOfInput:
                    throw new InvalidPdfContentException($"The dictionary that starts before offset {token.Start} is not closed");
                case PdfTokenKind.Name:
                    break;
                case PdfTokenKind.Keyword when IsStructureKeyword(Lexer.GetBytes(token)):
                    Lexer.Position = token.Start;
                    return dictionary;
                default:
                    // A key that is not a name: skip it and its value when it is a container
                    if (token.Kind is PdfTokenKind.ArrayStart or PdfTokenKind.DictionaryStart)
                    {
                        ParseValue(token, depth + 1);
                    }

                    continue;
            }

            var key = PdfName.FromBytes(Lexer.DecodeName(token));
            var valueToken = Lexer.Next();
            if (valueToken.Kind == PdfTokenKind.DictionaryEnd)
            {
                // A key without value: the entry is null
                return dictionary;
            }

            var value = ParseValue(valueToken, depth + 1);
            if (value is null)
            {
                Lexer.Position = valueToken.Start;
                if (valueToken.Kind == PdfTokenKind.Keyword)
                    return dictionary;

                continue;
            }

            dictionary.SetParsed(key, value);
        }
    }

    private static bool IsStructureKeyword(ReadOnlySpan<byte> bytes)
    {
        return bytes.SequenceEqual("endobj"u8) || bytes.SequenceEqual("stream"u8) || bytes.SequenceEqual("endstream"u8) || bytes.SequenceEqual("obj"u8)
            || bytes.SequenceEqual("xref"u8) || bytes.SequenceEqual("trailer"u8) || bytes.SequenceEqual("startxref"u8);
    }
}
