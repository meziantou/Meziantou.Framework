using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Content;

/// <summary>
/// Reads the operations of a content stream (ISO 32000-2, 7.8.2): operands followed by an operator. An inline image
/// (8.9.7) is returned as the operator <c>BI</c> with its dictionary and its data (a string) as operands.
/// </summary>
/// <remarks>
/// The numbers, names and strings, and the arrays that contain only such tokens (the arrays of <c>TJ</c>), are kept as
/// tokens of the content (<see cref="ContentOperand"/>): they become objects only when asked for. The other containers
/// are parsed as objects, as <see cref="PdfObjectParser"/> parses them.
/// </remarks>
internal sealed class ContentStreamReader
{
    private const int MaxOperands = 4096;

    private static readonly Dictionary<string, string> Operators = CreateOperators();
    private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> OperatorLookup = Operators.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly PdfLexer _lexer;
    private readonly PdfObjectParser _parser;
    private readonly int _maxDepth;

    private ContentOperand[] _operands = new ContentOperand[8];
    private int _operandCount;

    // The items of the arrays of tokens of the current operation
    private PdfToken[] _items = [];
    private int _itemCount;

    // The bytes of the last string decoded
    private byte[]? _stringBuffer;

    // The list the operands are added to, when they are read as objects
    private List<PdfObject>? _objects;

    public ContentStreamReader(ReadOnlyMemory<byte> data, int maxDepth = PdfReadOptions.DefaultMaxNestingDepth)
    {
        _lexer = new PdfLexer(data);
        _parser = new PdfObjectParser(_lexer, objects: null, maxDepth);
        _maxDepth = maxDepth;
    }

    /// <summary>Gets the offset in the data after the last operation read: the bytes of an operation are those between two positions.</summary>
    public int Position => _lexer.Position;

    /// <summary>Gets the operands of the last operation read, valid until the next operation is read.</summary>
    public ReadOnlySpan<ContentOperand> Operands => _operands.AsSpan(0, _operandCount);

    /// <summary>Reads the next operation.</summary>
    /// <param name="operands">The list that receives the operands as objects; it is cleared first.</param>
    /// <param name="name">The operator.</param>
    /// <returns><see langword="false"/> at the end of the content.</returns>
    public bool TryRead(List<PdfObject> operands, out string name)
    {
        // The operands are added to the list as they are read, as objects: the arrays are parsed, not kept as tokens
        operands.Clear();
        _objects = operands;
        try
        {
            return TryRead(out name);
        }
        finally
        {
            _objects = null;
        }
    }

    /// <summary>Reads the next operation: its operands are in <see cref="Operands"/>.</summary>
    /// <param name="name">The operator.</param>
    /// <returns><see langword="false"/> at the end of the content.</returns>
    public bool TryRead(out string name)
    {
        _operandCount = 0;
        _itemCount = 0;
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
                        ReadInlineImage();
                    }
                    else if (name is "true" or "false" or "null")
                    {
                        AddObject(name switch { "true" => PdfBoolean.True, "false" => PdfBoolean.False, _ => PdfNull.Instance });
                        continue;
                    }

                    return true;
                case PdfTokenKind.ArrayEnd or PdfTokenKind.DictionaryEnd or PdfTokenKind.Invalid or PdfTokenKind.ProcedureStart or PdfTokenKind.ProcedureEnd:
                    // A delimiter alone: skipped
                    continue;
                case PdfTokenKind.Integer or PdfTokenKind.Real or PdfTokenKind.Name or PdfTokenKind.LiteralString or PdfTokenKind.HexString:
                    if (OperandCount < MaxOperands)
                    {
                        if (_objects is { } objects)
                        {
                            objects.Add(GetObject(token));
                        }
                        else
                        {
                            AddOperand(ContentOperand.FromToken(token));
                        }
                    }

                    continue;
                case PdfTokenKind.ArrayStart when _objects is null && TryReadTokenArray(token, out var array):
                    if (_operandCount < MaxOperands)
                    {
                        AddOperand(array);
                    }
                    else
                    {
                        _itemCount = array.ItemStart;
                    }

                    continue;
            }

            // A dictionary, or an array that contains containers or is not closed
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

            if (value is not null && OperandCount < MaxOperands)
            {
                AddObject(value);
            }
        }
    }

    /// <summary>Gets an operand as an object, as <see cref="PdfObjectParser"/> parses it.</summary>
    public PdfObject GetObject(in ContentOperand operand)
    {
        if (operand.Object is { } value)
            return value;

        var token = operand.Token;
        if (token.Kind is PdfTokenKind.ArrayStart)
        {
            var items = _items.AsSpan(operand.ItemStart, operand.ItemCount);
            var list = new List<PdfObject>(items.Length);
            foreach (var item in items)
            {
                list.Add(GetObject(item));
            }

            return PdfArray.Wrap(list);
        }

        return GetObject(token);
    }

    /// <summary>Gets the bytes of a string operand, or of a string item of an array.</summary>
    /// <param name="operand">The operand.</param>
    /// <param name="bytes">The bytes, valid until the next string is decoded or the next operation is read.</param>
    /// <returns><see langword="false"/> when the operand is not a string.</returns>
    public bool TryGetString(in ContentOperand operand, out ReadOnlySpan<byte> bytes)
    {
        switch (operand.Token.Kind)
        {
            case PdfTokenKind.LiteralString:
                bytes = _lexer.DecodeLiteralString(operand.Token, ref _stringBuffer);
                return true;
            case PdfTokenKind.HexString:
                bytes = _lexer.DecodeHexString(operand.Token, ref _stringBuffer);
                return true;
        }

        if (operand.Object is PdfString text)
        {
            bytes = text.Span;
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>Gets a name operand.</summary>
    /// <returns><see langword="false"/> when the operand is not a name.</returns>
    public bool TryGetName(in ContentOperand operand, [NotNullWhen(true)] out PdfName? name)
    {
        if (operand.Token.Kind is PdfTokenKind.Name)
        {
            name = ReadName(operand.Token);
            return true;
        }

        name = operand.Object as PdfName;
        return name is not null;
    }

    /// <summary>Gets the items of an array operand.</summary>
    /// <returns><see langword="false"/> when the operand is not an array.</returns>
    public bool TryGetArray(in ContentOperand operand, out ContentArray array)
    {
        if (operand.IsTokenArray)
        {
            array = new ContentArray(_items, operand.ItemStart, operand.ItemCount);
            return true;
        }

        if (operand.Object is PdfArray objects)
        {
            array = new ContentArray(objects);
            return true;
        }

        array = default;
        return false;
    }

    private PdfObject GetObject(PdfToken token)
    {
        switch (token.Kind)
        {
            case PdfTokenKind.Integer:
                return PdfInteger.Get(token.IntegerValue);
            case PdfTokenKind.Real:
                return PdfReal.Get(token.RealValue);
            case PdfTokenKind.Name:
                return ReadName(token);
            case PdfTokenKind.LiteralString:
                return new PdfString(_lexer.DecodeLiteralString(token), isHexadecimal: false);
            case PdfTokenKind.HexString:
                return new PdfString(_lexer.DecodeHexString(token), isHexadecimal: true);
            default:
                // A keyword in an array: an unknown keyword is null, as the parser reads it
                var bytes = _lexer.GetBytes(token);
                if (bytes.SequenceEqual("true"u8))
                    return PdfBoolean.True;

                if (bytes.SequenceEqual("false"u8))
                    return PdfBoolean.False;

                return PdfNull.Instance;
        }
    }

    private PdfName ReadName(PdfToken token)
    {
        var length = PdfLexer.GetNameLength(token);
        var buffer = length <= 128 ? stackalloc byte[128] : new byte[length];
        return PdfName.FromBytes(buffer[.._lexer.DecodeName(token, buffer)]);
    }

    /// <summary>
    /// Reads an array whose items are numbers, names, strings or keywords, as the parser would read it. The items are kept
    /// as tokens; an array that contains a container, that a keyword such as <c>endobj</c> ends, or that is not closed, is
    /// left to the parser.
    /// </summary>
    private bool TryReadTokenArray(PdfToken start, out ContentOperand array)
    {
        var itemStart = _itemCount;

        // The parser checks the depth of the items: deeper than the limit, it throws
        if (_maxDepth >= 1)
        {
            while (true)
            {
                var token = _lexer.Next();
                switch (token.Kind)
                {
                    case PdfTokenKind.ArrayEnd:
                        array = ContentOperand.FromTokenArray(start.Start, token.End, itemStart, _itemCount - itemStart);
                        return true;
                    case PdfTokenKind.Integer or PdfTokenKind.Real or PdfTokenKind.Name or PdfTokenKind.LiteralString or PdfTokenKind.HexString:
                    case PdfTokenKind.Keyword when !PdfObjectParser.IsStructureKeyword(_lexer.GetBytes(token)):
                        AddItem(token);
                        continue;
                    case PdfTokenKind.DictionaryEnd or PdfTokenKind.Invalid or PdfTokenKind.ProcedureStart or PdfTokenKind.ProcedureEnd:
                        // A delimiter alone: the parser skips it
                        continue;
                }

                break;
            }
        }

        _itemCount = itemStart;
        _lexer.Position = start.End;
        array = default;
        return false;
    }

    private int OperandCount => _objects?.Count ?? _operandCount;

    private void AddObject(PdfObject value)
    {
        if (_objects is { } objects)
        {
            objects.Add(value);
        }
        else
        {
            AddOperand(ContentOperand.FromObject(value));
        }
    }

    private void AddOperand(ContentOperand operand)
    {
        if (_operandCount == _operands.Length)
        {
            Array.Resize(ref _operands, _operands.Length * 2);
        }

        _operands[_operandCount++] = operand;
    }

    private void AddItem(PdfToken token)
    {
        if (_itemCount == _items.Length)
        {
            Array.Resize(ref _items, Math.Max(16, _items.Length * 2));
        }

        _items[_itemCount++] = token;
    }

    private void ReadInlineImage()
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
            var position = _lexer.Position;
            var valueToken = _lexer.Next();
            if (valueToken.Kind == PdfTokenKind.Keyword && _lexer.GetBytes(valueToken).SequenceEqual("ID"u8))
                break;

            PdfObject? value;
            try
            {
                value = _parser.ParseValue(valueToken, depth: 0);
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or PdfResourceLimitException)
            {
                // A value that is not closed or nested too deep: the key has no value, and the tokens after it are read
                // again one at a time until the ID operator, so that the content after the image is still read
                _lexer.Position = position;
                value = null;
            }

            if (value is not null)
            {
                dictionary.SetParsed(key, value);
            }
        }

        // A single whitespace character follows ID; the data ends at /L bytes (PDF 2.0), or before the whitespace before "EI"
        var span = _lexer.Span;
        var start = Math.Min(_lexer.Position + 1, span.Length);
        var (dataEnd, endOfImage) = FindEndOfInlineImage(span, start, dictionary);
        AddObject(dictionary);
        AddObject(PdfString.FromBytes(span[start..dataEnd], hexadecimal: false));
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
