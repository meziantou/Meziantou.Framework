using System.Globalization;

namespace Meziantou.Framework.Mermaid.Internals.VennDiagrams;

/// <summary>
/// Parses the <c>venn-beta</c> diagrams of Mermaid with its grammar (<c>venn.jison</c>): <c>set</c>, <c>union</c>,
/// <c>text</c> (or an indented <c>text</c> under a set or a union), <c>style</c> and <c>title</c> statements, which need
/// no line break between them. A union of sets not declared by an earlier <c>set</c> is an error, as in Mermaid.
/// </summary>
internal sealed class VennDiagramParser
{
    /// <summary>The maximum number of sets: the layout takes a time that grows quickly with them.</summary>
    internal const int MaxSets = 32;

    /// <summary>The maximum width and height read from the configuration.</summary>
    private const double MaxSize = 100_000;

    private readonly ParseContext _context;
    private readonly VennDiagramLexer _lexer;
    private readonly VennDiagram _diagram = new();
    private readonly HashSet<string> _knownSets = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _currentSets;
    private VennToken? _lookahead;

    private VennDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _lexer = new VennDiagramLexer(context, position);
    }

    public static VennDiagram Parse(ParseContext context, int position) => new VennDiagramParser(context, position).Parse();

    private VennDiagram Parse()
    {
        ApplyConfiguration();
        while (Peek().Kind is VennTokenKind.Newline)
        {
            Read();
        }

        Expect(VennTokenKind.Venn, "'venn-beta'");
        while (true)
        {
            var token = Peek();
            switch (token.Kind)
            {
                case VennTokenKind.EndOfFile:
                    return _diagram;
                case VennTokenKind.Newline:
                    Read();
                    break;
                case VennTokenKind.Title:
                    Read();
                    var title = Decode(VennDiagramLexer.Trim(token.Value));
                    _diagram.Title = title.Length is 0 ? null : title;
                    break;
                case VennTokenKind.Set:
                    ParseSet();
                    break;
                case VennTokenKind.Union:
                    ParseUnion();
                    break;
                case VennTokenKind.Text:
                    ParseText();
                    break;
                case VennTokenKind.IndentText:
                    ParseIndentedText();
                    break;
                case VennTokenKind.Style:
                    ParseStyle();
                    break;
                default:
                    throw Unexpected(token, "a statement");
            }
        }
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "venn");
        var configuration = _context.Configuration;
        // Mermaid requires a size of at least 1; a huge size would give texts too large to measure
        if (configuration.GetNumber("venn", "width") is >= 1 and <= MaxSize and var width)
        {
            _diagram.Width = width;
        }

        if (configuration.GetNumber("venn", "height") is >= 1 and <= MaxSize and var height)
        {
            _diagram.Height = height;
        }

        for (var i = 0; i < 8; i++)
        {
            if (ReadColor("venn" + (i + 1).ToString(CultureInfo.InvariantCulture)) is { } color)
            {
                _diagram.SetColors[i] = color;
            }
        }

        _diagram.TextColor = ReadColor("vennSetTextColor");

        SceneColor? ReadColor(string name) => configuration.GetString("themeVariables", name) is { } value && SceneColor.TryParse(value.Trim(), out var color) ? color : default(SceneColor?);
    }

    /// <summary>Parses <c>set id ["label"] [: size]</c>.</summary>
    private void ParseSet()
    {
        var start = Read();
        var id = ParseIdentifier();
        var (label, size) = ParseLabelAndSize();
        if (!_knownSets.Contains(id) && _knownSets.Count >= MaxSets)
            throw _lexer.Error(start.Offset, $"A Venn diagram can have at most {MaxSets} sets.");

        _knownSets.Add(id);
        AddArea(start, [id], label, size);
    }

    /// <summary>Parses <c>union a, b ["label"] [: size]</c>: the sets must be declared by an earlier <c>set</c>.</summary>
    private void ParseUnion()
    {
        var start = Read();
        var sets = ParseIdentifierList();
        var (label, size) = ParseLabelAndSize();
        if (sets.Count < 2)
            throw _lexer.Error(start.Offset, "A union requires multiple identifiers.");

        if (sets.Count > MaxSets)
            throw _lexer.Error(start.Offset, $"A union can have at most {MaxSets} sets.");

        var unknown = sets.Where(set => !_knownSets.Contains(set)).ToList();
        if (unknown.Count > 0)
            throw _lexer.Error(start.Offset, $"Unknown set identifier: {string.Join(", ", unknown)}.");

        AddArea(start, sets, label, size);
    }

    private void AddArea(VennToken start, List<string> sets, string? label, double? size)
    {
        CheckCount(start);
        var area = new VennArea(sets, label, size);
        _diagram.Areas.Add(area);
        _currentSets = area.Sets;

        // An indented text on the next lines is a text of this area
        _lexer.IndentMode = true;
    }

    /// <summary>Parses <c>text a, b id ["label"]</c>.</summary>
    private void ParseText()
    {
        var start = Read();
        var sets = ParseIdentifierList();
        var (id, label) = ParseTextNode();
        CheckCount(start);
        _diagram.TextNodes.Add(new VennTextNode(sets, id, label));
    }

    /// <summary>Parses an indented <c>text id ["label"]</c>, a text of the last set or union.</summary>
    private void ParseIndentedText()
    {
        var start = Read();
        var (id, label) = ParseTextNode();
        if (_currentSets is null)
            throw _lexer.Error(start.Offset, "An indented text requires a set.");

        CheckCount(start);
        _diagram.TextNodes.Add(new VennTextNode(_currentSets, id, label));
    }

    /// <summary>Parses the identifier of a text (a word, a string or a number) and its label; a number has no label.</summary>
    private (string Id, string? Label) ParseTextNode()
    {
        var token = Read();
        switch (token.Kind)
        {
            case VennTokenKind.Identifier:
            case VennTokenKind.String:
                var id = token.Kind is VennTokenKind.String ? UnquoteString(token.Value) : token.Value;
                string? label = null;
                if (Peek().Kind is VennTokenKind.BracketLabel)
                {
                    label = ParseLabel(Read());
                }

                return (id, label);
            case VennTokenKind.Numeric:
                return (token.Value, null);
            default:
                throw Unexpected(token, "the identifier of a text");
        }
    }

    /// <summary>Parses <c>style a, b key: value, key: value</c>.</summary>
    private void ParseStyle()
    {
        Read();
        var targets = ParseIdentifierList();
        var style = new VennStyle();
        while (true)
        {
            var key = Expect(VennTokenKind.Identifier, "the name of a style property");
            Expect(VennTokenKind.Colon, "':'");
            var valueToken = Read();
            string value;
            if (valueToken.Kind is VennTokenKind.String)
            {
                // A quoted value loses its quotes
                value = UnquoteString(valueToken.Value);
            }
            else if (IsValueToken(valueToken.Kind))
            {
                var parts = new List<string> { valueToken.Value };
                while (IsValueToken(Peek().Kind))
                {
                    parts.Add(Read().Value);
                }

                value = string.Join(' ', parts);
            }
            else
            {
                throw Unexpected(valueToken, "a style value");
            }

            style = ApplyStyleProperty(style, key, value);
            if (Peek().Kind is not VennTokenKind.Comma)
                break;

            Read();
        }

        if (!style.IsEmpty)
        {
            _diagram.Styles.Add(new VennStyleRule(targets, style));
        }

        static bool IsValueToken(VennTokenKind kind) => kind is VennTokenKind.Identifier or VennTokenKind.Numeric or VennTokenKind.HexColor or VennTokenKind.RgbColor or VennTokenKind.RgbaColor;
    }

    /// <summary>Applies a CSS property of a style statement: <c>fill</c>, <c>fill-opacity</c>, <c>stroke</c>, <c>stroke-width</c> and <c>color</c> are the ones Mermaid reads.</summary>
    private VennStyle ApplyStyleProperty(VennStyle style, VennToken key, string value)
    {
        switch (key.Value)
        {
            case "fill" when ParseColor(value) is { } fill:
                return style with { Fill = fill };
            case "stroke" when ParseColor(value) is { } stroke:
                return style with { Stroke = stroke };
            case "color" when ParseColor(value) is { } color:
                return style with { TextColor = color };
            case "fill-opacity" when ParseNumber(value, allowPercent: true, allowPixels: false) is { } opacity:
                return style with { FillOpacity = opacity };
            case "stroke-width" when ParseNumber(value, allowPercent: false, allowPixels: true) is >= 0 and var width:
                return style with { StrokeWidth = width };
            case "fill" or "stroke" or "color" or "fill-opacity" or "stroke-width":
                _context.Warn(key.Offset, $"The value '{value}' of the style property '{key.Value}' is not valid and is ignored.");
                return style;
            default:
                _context.Warn(key.Offset, $"The style property '{key.Value}' is not used by Venn diagrams and is ignored.");
                return style;
        }
    }

    private static SceneColor? ParseColor(string value) => SceneColor.TryParse(value.Trim(), out var color) ? color : default(SceneColor?);

    private static double? ParseNumber(string value, bool allowPercent, bool allowPixels)
    {
        value = value.Trim();
        var divisor = 1.0;
        if (allowPercent && value.EndsWith('%', StringComparison.Ordinal))
        {
            value = value[..^1];
            divisor = 100;
        }
        else if (allowPixels && value.EndsWith("px", StringComparison.Ordinal))
        {
            value = value[..^2];
        }

        if (value.Length is 0 || !IsNumberCharacter(value[^1]) || !double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return null;

        number /= divisor;
        return double.IsFinite(number) ? number : null;

        static bool IsNumberCharacter(char c) => char.IsAsciiDigit(c) || c == '.';
    }

    /// <summary>Parses the optional <c>["label"]</c> and <c>: size</c> of a set or a union.</summary>
    private (string? Label, double? Size) ParseLabelAndSize()
    {
        string? label = null;
        double? size = null;
        if (Peek().Kind is VennTokenKind.BracketLabel)
        {
            label = ParseLabel(Read());
        }

        if (Peek().Kind is VennTokenKind.Colon)
        {
            Read();
            var number = Expect(VennTokenKind.Numeric, "a size");
            var value = double.Parse(number.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

            // Mermaid accepts these sizes, but draws nothing sensible with them
            if (!(value >= 0))
                throw _lexer.Error(number.Offset, "The size must be a positive number.");

            if (!double.IsFinite(value))
                throw _lexer.Error(number.Offset, "The size is too large.");

            size = value;
        }

        return (label, size);
    }

    /// <summary>
    /// Reads a label: <c>["text"]</c> or <c>[text]</c>, trimmed. An empty label is <see langword="null"/> (<c>[""]</c>,
    /// <c>[ ]</c>), but a label of spaces between quotes is empty (<c>[" "]</c>), as Mermaid does.
    /// </summary>
    private static string? ParseLabel(VennToken token)
    {
        var value = token.Value;
        if (value.StartsWith("[\"", StringComparison.Ordinal))
        {
            var quoted = value[2..^2];
            return quoted.Length is 0 ? null : Decode(VennDiagramLexer.Trim(quoted));
        }

        var trimmed = VennDiagramLexer.Trim(value[1..^1]);
        return trimmed.Length is 0 ? null : Decode(trimmed);
    }

    private List<string> ParseIdentifierList()
    {
        var list = new List<string> { ParseIdentifier() };
        while (Peek().Kind is VennTokenKind.Comma)
        {
            Read();
            list.Add(ParseIdentifier());
        }

        return list;
    }

    /// <summary>Parses an identifier: a word, or a string without its quotes.</summary>
    private string ParseIdentifier()
    {
        var token = Read();
        return token.Kind switch
        {
            VennTokenKind.Identifier => token.Value,
            VennTokenKind.String => UnquoteString(token.Value),
            _ => throw Unexpected(token, "an identifier"),
        };
    }

    /// <summary>Removes the quotes of a string token, and decodes its entity codes.</summary>
    private static string UnquoteString(string value) => Decode(value[1..^1]);

    private static string Decode(string value) => MermaidText.DecodeEntities(value);

    private void CheckCount(VennToken start)
    {
        if (_diagram.Areas.Count + _diagram.TextNodes.Count >= _context.Options.MaxNodes)
            throw _lexer.Error(start.Offset, $"The diagram has more than {_context.Options.MaxNodes} sets, unions and texts.");
    }

    private VennToken Peek() => _lookahead ??= _lexer.Next();

    private VennToken Read()
    {
        var token = Peek();
        _lookahead = null;
        return token;
    }

    private VennToken Expect(VennTokenKind kind, string description)
    {
        var token = Read();
        if (token.Kind != kind)
            throw Unexpected(token, description);

        return token;
    }

    private MermaidParseException Unexpected(VennToken token, string expected)
    {
        var found = token.Kind switch
        {
            VennTokenKind.EndOfFile => "the end of the text",
            VennTokenKind.Newline => "a line break",
            VennTokenKind.IndentText => "an indented text",
            _ => $"'{token.Value}'",
        };

        return _lexer.Error(token.Offset, $"Expected {expected}, found {found}.");
    }
}
