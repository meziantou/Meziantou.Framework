using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Parses objects (ISO 32000-2, 7.3) and indirect objects (7.3.10) from tokens.</summary>
internal sealed class PdfObjectParser
{
    private const int MaxWhitespaceAfterStream = 256;

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

    /// <summary>Gets or sets the names the reader already parsed, shared by the objects of a file.</summary>
    public PdfNameCache? NameCache { get; set; }

    /// <summary>Gets or sets where the data of the lexer is in the input of the file, when the parser reads a file.</summary>
    public PdfInputWindow? Window { get; set; }

    /// <summary>Gets a value indicating whether the data of the last stream ends after the window: the stream is complete, the bytes after it are not read.</summary>
    public bool StreamEndsAfterWindow { get; private set; }

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
                return ParseName(token);
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
            ReadStreamData(stream, dictionary);
            value = stream;
            if (StreamEndsAfterWindow)
                return value;

            position = Lexer.Position;
            next = Lexer.Next();
        }

        if (!Lexer.IsKeyword(next, "endobj"u8))
        {
            Lexer.Position = position;
        }

        return value;
    }

    /// <summary>Gets the offset of the data of a stream: the <c>stream</c> keyword is followed by CRLF or LF (7.3.8.1); a CR alone and spaces before the end of line are accepted.</summary>
    internal static int GetStreamDataStart(ReadOnlySpan<byte> span, int position)
    {
        while (position < span.Length && span[position] is (byte)' ' or (byte)'\t')
        {
            position++;
        }

        if (position < span.Length && span[position] == '\r')
        {
            position++;
        }

        if (position < span.Length && span[position] == '\n')
        {
            position++;
        }

        return position;
    }

    private void ReadStreamData(PdfStream stream, PdfDictionary dictionary)
    {
        var span = Lexer.Span;
        var start = GetStreamDataStart(span, Lexer.Position);
        if (start >= span.Length)
        {
            Lexer.ReachedEnd = true;
        }

        var declaredLength = dictionary.GetInteger(PdfNames.Length, -1);
        var available = Window is { } window ? window.Limit - (window.Offset + start) : span.Length - start;
        if (declaredLength >= 0 && declaredLength <= available)
        {
            if (declaredLength > Array.MaxLength)
                throw new UnsupportedPdfFeatureException("A stream larger than 2 GB is not supported");

            // The keyword that follows the data, after an optional end of line (7.3.8.1)
            var end = start + declaredLength;
            var after = GetBytesAfter(end, MaxWhitespaceAfterStream + 16);
            var whitespace = 0;
            while (whitespace < after.Length && PdfLexer.IsWhitespace(after[whitespace]))
            {
                whitespace++;
            }

            after = after[whitespace..];
            if (after.StartsWith("endstream"u8))
            {
                SetStreamData(stream, start, (int)declaredLength);
                MoveTo(end + whitespace + "endstream".Length);
                return;
            }

            // The endstream keyword is missing, but the data ends where the object or the next object starts: the length
            // is right, and the search of a keyword below would read the objects that follow
            if (after.StartsWith("endobj"u8) || IsObjectHeader(after))
            {
                SetStreamData(stream, start, (int)declaredLength);
                MoveTo(end + whitespace);
                return;
            }
        }

        // The length is missing or wrong: the data ends at the "endstream" keyword, without the end of line before it. In a
        // recovered file, the data of the lexer ends at the next object, so that the search does not read the file again for
        // each damaged object
        var data = span[start..];
        var endstream = data.IndexOf("endstream"u8);
        int dataEnd;
        if (endstream >= 0)
        {
            dataEnd = start + endstream;
            Lexer.Position = dataEnd + "endstream".Length;
        }
        else
        {
            var endobj = data.IndexOf("endobj"u8);
            dataEnd = endobj >= 0 ? start + endobj : span.Length;
            Lexer.Position = dataEnd;
            Lexer.ReachedEnd = true;
        }

        if (dataEnd > start && span[dataEnd - 1] == '\n')
        {
            dataEnd--;
        }

        if (dataEnd > start && span[dataEnd - 1] == '\r')
        {
            dataEnd--;
        }

        SetStreamData(stream, start, dataEnd - start);
    }

    /// <summary>Gets the bytes at a position relative to the data of the lexer, which may be after the window of the input.</summary>
    private ReadOnlySpan<byte> GetBytesAfter(long position, int count)
    {
        var span = Lexer.Span;
        if (position + count <= span.Length || Window is null)
            return position >= span.Length ? [] : span[(int)position..(int)Math.Min(span.Length, position + count)];

        var buffer = new byte[count];
        return buffer.AsSpan(0, Window.Input.Read(Window.Offset + position, buffer));
    }

    private void SetStreamData(PdfStream stream, int start, int length)
    {
        var span = Lexer.Span;
        if ((long)start + length <= span.Length || Window is null)
        {
            // The window of a stream input is reused: the data is copied
            stream.SetLoadedData(Window is null || Window.Input.IsInMemory ? Lexer.Data.Slice(start, length) : span.Slice(start, length).ToArray());
            return;
        }

        // The data is after the window: it is read from the input when it is first used
        var input = Window.Input;
        var offset = Window.Offset + start;
        stream.SetLoadedData(() => input.GetMemory(offset, length));
    }

    /// <summary>Moves after the data of a stream; a position after the window of the input ends the parsing of the object.</summary>
    private void MoveTo(long position)
    {
        if (position <= Lexer.Span.Length)
        {
            Lexer.Position = (int)position;
            return;
        }

        Lexer.Position = Lexer.Span.Length;
        StreamEndsAfterWindow = true;
    }

    /// <summary>Indicates whether the bytes start with an object header (<c>12 0 obj</c>).</summary>
    private static bool IsObjectHeader(ReadOnlySpan<byte> span)
    {
        var position = 0;
        for (var part = 0; part < 2; part++)
        {
            var digits = 0;
            while (position < span.Length && span[position] is >= (byte)'0' and <= (byte)'9')
            {
                position++;
                digits++;
            }

            if (digits == 0 || position >= span.Length || !PdfLexer.IsWhitespace(span[position]))
                return false;

            while (position < span.Length && PdfLexer.IsWhitespace(span[position]))
            {
                position++;
            }
        }

        return span[position..].StartsWith("obj"u8) && (position + 3 >= span.Length || !PdfLexer.IsRegular(span[position + 3]));
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
                case PdfTokenKind.EndOfInput when IsAtRecoveryBoundary():
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
                case PdfTokenKind.EndOfInput when IsAtRecoveryBoundary():
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

            var key = ParseName(token);
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

    /// <summary>
    /// Indicates whether the data ends at the next object or trailer the recovery found: a container that is not closed
    /// there ends with the data, as it would end at the header of the next object if the data went on.
    /// </summary>
    private bool IsAtRecoveryBoundary() => Window is { } window && window.Limit < window.Input.Length && window.Offset + Lexer.Length >= window.Limit;

    private PdfName ParseName(PdfToken token) => NameCache is { } cache ? cache.Get(Lexer, token) : PdfName.FromBytes(Lexer.DecodeName(token));

    private static bool IsStructureKeyword(ReadOnlySpan<byte> bytes)
    {
        return bytes.SequenceEqual("endobj"u8) || bytes.SequenceEqual("stream"u8) || bytes.SequenceEqual("endstream"u8) || bytes.SequenceEqual("obj"u8)
            || bytes.SequenceEqual("xref"u8) || bytes.SequenceEqual("trailer"u8) || bytes.SequenceEqual("startxref"u8);
    }
}
