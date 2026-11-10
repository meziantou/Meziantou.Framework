using System.Globalization;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.PacketDiagrams;

/// <summary>Parses the <c>packet</c> diagrams of Mermaid: fields of contiguous bits, as <c>0-15: "Source port"</c> or <c>+8: "Flags"</c>.</summary>
internal sealed class PacketDiagramParser
{
    /// <summary>The number of rows Mermaid draws at most.</summary>
    private const int MaxRows = 10_000;

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly PacketDiagram _diagram = new();

    private PacketDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static PacketDiagram Parse(ParseContext context, int position) => new PacketDiagramParser(context, position).Parse();

    private PacketDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("packet-beta") && !_scanner.TryConsume("packet"))
            throw _scanner.Error(keywordStart, "'packet' is expected.");

        ExpectLineEnd();
        ApplyConfiguration();
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            if (MermaidCommon.TryParseAccessibility(_scanner, _diagram) || GitGraphLangium.TryParseTitle(_scanner, _diagram))
                continue;

            ParseField();
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "packet");
        var configuration = _context.Configuration;
        if (configuration.GetNumber("packet", "bitsPerRow") is { } bitsPerRow)
        {
            if (bitsPerRow is >= 1 and <= 1024 && bitsPerRow == Math.Floor(bitsPerRow))
            {
                _diagram.BitsPerRow = (int)bitsPerRow;
            }
            else
            {
                _context.Warn(0, "The configuration 'packet.bitsPerRow' must be an integer between 1 and 1024: it is ignored.");
            }
        }

        if (configuration.GetNumber("packet", "bitWidth") is >= 1 and <= 1000 and var bitWidth)
        {
            _diagram.BitWidth = bitWidth;
        }

        if (configuration.GetNumber("packet", "rowHeight") is >= 1 and <= 1000 and var rowHeight)
        {
            _diagram.RowHeight = rowHeight;
        }

        if (configuration.GetBoolean("packet", "showBits") is { } showBits)
        {
            _diagram.ShowBits = showBits;
        }

        if (configuration.GetString("packet", "bitOrder") is { } bitOrder)
        {
            _diagram.DescendingBitOrder = bitOrder switch
            {
                "descending" => true,
                "ascending" => false,
                _ => WarnBitOrder(bitOrder),
            };
        }
    }

    private bool WarnBitOrder(string bitOrder)
    {
        _context.Warn(0, $"The bit order '{bitOrder}' is not supported: use 'ascending' or 'descending'.");
        return false;
    }

    private void ParseField()
    {
        var start = _scanner.Position;
        int first;
        int last;
        var lastBit = _diagram.Fields.Count is 0 ? -1 : _diagram.Fields[^1].End;
        if (_scanner.TryConsume("+"))
        {
            _scanner.SkipSpaces();
            var bits = ReadInteger();
            if (bits is 0)
                throw _scanner.Error(start, $"The field {lastBit + 1} is not valid: a field cannot have zero bits.");

            first = lastBit + 1;
            last = checked(first + bits - 1);
        }
        else
        {
            first = ReadInteger();
            _scanner.SkipSpaces();
            if (_scanner.TryConsume("-"))
            {
                _scanner.SkipSpaces();
                last = ReadInteger();
                if (last < first)
                    throw _scanner.Error(start, $"The field {first}-{last} is not valid: its end must be greater than its start.");
            }
            else
            {
                last = first;
            }

            if (first != lastBit + 1)
                throw _scanner.Error(start, $"The field {first}-{last} is not contiguous: it should start at {lastBit + 1}.");
        }

        _scanner.SkipSpaces();
        _scanner.Expect(":");
        _scanner.SkipSpaces();
        if (_scanner.Current is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted label is expected.");

        var label = GitGraphLangium.ReadString(_scanner);
        ExpectLineEnd();
        if (last / (long)_diagram.BitsPerRow >= MaxRows)
            throw _scanner.Error(start, $"The packet has more than {MaxRows} rows.");

        if (_diagram.Fields.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} fields.");

        _diagram.Fields.Add(new PacketField(first, last, new DiagramText(label)));
    }

    private int ReadInteger()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiDigit(_scanner.Current))
        {
            _scanner.Position++;
        }

        var text = _scanner.Text[start.._scanner.Position];
        if (text.Length is 0)
            throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': a number is expected.");

        if (text.Length > 1 && text[0] == '0')
            throw _scanner.Error(start, $"'{text}' is not a valid number: it cannot start with 0.");

        if (_scanner.Current == '.')
            throw _scanner.Error(start, $"'{text}.' is not a valid number: a number of bits is an integer.");

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > int.MaxValue / 2)
            throw _scanner.Error(start, $"The number '{text}' is too large.");

        return value;
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }
}
