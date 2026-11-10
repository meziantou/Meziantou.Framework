using System.Globalization;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.CynefinDiagrams;

/// <summary>
/// Parses the <c>cynefin-beta</c> diagrams of Mermaid with its Langium grammar: domain blocks (<c>complex</c> followed by
/// quoted items), transitions (<c>complex --> complicated : "label"</c>), the title and the accessibility statements. A
/// domain declared twice keeps the items of its last block, and a transition to the same domain is ignored, as in Mermaid.
/// </summary>
internal sealed class CynefinDiagramParser
{
    private static readonly (string Name, CynefinDomain Domain)[] Domains =
    [
        ("complex", CynefinDomain.Complex),
        ("complicated", CynefinDomain.Complicated),
        ("clear", CynefinDomain.Clear),
        ("chaotic", CynefinDomain.Chaotic),
        ("confusion", CynefinDomain.Confusion),
    ];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly CynefinDiagram _diagram = new();

    private CynefinDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static CynefinDiagram Parse(ParseContext context, int position) => new CynefinDiagramParser(context, position).Parse();

    private CynefinDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("cynefin-beta"))
            throw _scanner.Error(keywordStart, "'cynefin-beta' is expected.");

        // The keyword ends with a colon, a space, a comment or the end of the line
        if (!_scanner.TryConsume(":") && !_scanner.IsEnd && _scanner.Current is not (' ' or '\t' or '\n') && !_scanner.IsCommentAt(_scanner.Position))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");

        ApplyConfiguration();
        while (true)
        {
            SkipHidden();
            if (_scanner.IsEnd)
                return _diagram;

            if (_scanner.Current == '\n')
            {
                _scanner.Position++;
                continue;
            }

            var start = _scanner.Position;
            if (MermaidCommon.TryParseAccessibility(_scanner, _diagram) || GitGraphLangium.TryParseTitle(_scanner, _diagram))
            {
                ExpectEndOfLine();
                continue;
            }

            if (TryReadDomain() is not { } domain)
                throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a domain (complex, complicated, clear, chaotic or confusion), a transition, a title or an accessibility statement is expected.");

            SkipHidden();
            if (_scanner.TryConsume("-->"))
            {
                ParseTransition(domain, start);
            }
            else
            {
                ParseDomainBlock(domain);
            }
        }
    }

    private void ParseDomainBlock(CynefinDomain domain)
    {
        // A new block of a domain replaces the items of the previous one
        for (var i = _diagram.Items.Count - 1; i >= 0; i--)
        {
            if (_diagram.Items[i].Domain == domain)
            {
                _diagram.Items.RemoveAt(i);
            }
        }

        while (true)
        {
            SkipHidden();
            if (_scanner.Current == '\n' && !_scanner.IsEnd)
            {
                _scanner.Position++;
                continue;
            }

            if (_scanner.IsEnd || _scanner.Current is not ('"' or '\''))
                return;

            var start = _scanner.Position;
            var label = GitGraphLangium.ReadString(_scanner);
            if (_diagram.Items.Count >= _context.Options.MaxNodes)
                throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} items.");

            _diagram.Items.Add(new CynefinItem(domain, label));
        }
    }

    private void ParseTransition(CynefinDomain from, int start)
    {
        SkipHidden();
        if (TryReadDomain() is not { } to)
            throw _scanner.Error($"Unexpected '{Describe()}': the domain the transition goes to is expected.");

        SkipHidden();
        string? label = null;
        if (_scanner.TryConsume(":"))
        {
            SkipHidden();
            if (_scanner.Current is not ('"' or '\'') || _scanner.IsEnd)
                throw _scanner.Error($"Unexpected '{Describe()}': a quoted label is expected.");

            label = GitGraphLangium.ReadString(_scanner);
        }

        ExpectEndOfLine();
        if (from == to)
        {
            _context.Warn(start, $"The transition from the domain '{GetName(from)}' to itself is ignored.");
            return;
        }

        if (_diagram.Transitions.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxEdges} transitions.");

        _diagram.Transitions.Add(new CynefinTransition(from, to, string.IsNullOrEmpty(label) ? null : new DiagramText(label)));
    }

    /// <summary>Reads a domain name; as the terminal of Mermaid, it needs no word boundary, so <c>complexity</c> is <c>complex</c> followed by an error.</summary>
    private CynefinDomain? TryReadDomain()
    {
        foreach (var (name, domain) in Domains)
        {
            if (_scanner.TryConsume(name))
                return domain;
        }

        return null;
    }

    /// <summary>Skips the spaces, the tabs and the <c>%%</c> comments, which the grammar hides, but not the line feeds.</summary>
    private void SkipHidden()
    {
        while (!_scanner.IsEnd)
        {
            if (_scanner.Current is ' ' or '\t')
            {
                _scanner.Position++;
            }
            else if (_scanner.IsCommentAt(_scanner.Position))
            {
                while (!_scanner.IsEnd && _scanner.Current != '\n')
                {
                    _scanner.Position++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private void ExpectEndOfLine()
    {
        SkipHidden();
        if (_scanner.IsEnd)
            return;

        if (_scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{Describe()}': the end of the line is expected.");

        _scanner.Position++;
    }

    private string Describe() => _scanner.Current == '\n' ? "end of line" : _scanner.Describe();

    internal static string GetName(CynefinDomain domain) => domain switch
    {
        CynefinDomain.Complex => "complex",
        CynefinDomain.Complicated => "complicated",
        CynefinDomain.Clear => "clear",
        CynefinDomain.Chaotic => "chaotic",
        _ => "confusion",
    };

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "cynefin");
        var configuration = _context.Configuration;
        ReadNumber("width", 1, 10_000, value => _diagram.Width = value);
        ReadNumber("height", 1, 10_000, value => _diagram.Height = value);
        ReadNumber("padding", 0, 1_000, value => _diagram.Padding = value);
        ReadNumber("boundaryAmplitude", 0, 50, value => _diagram.BoundaryAmplitude = value);
        if (configuration.GetBoolean("cynefin", "showDomainDescriptions") is { } showDescriptions)
        {
            _diagram.ShowDomainDescriptions = showDescriptions;
        }

        if (configuration.GetNumber("cynefin", "seed") is { } seed)
        {
            if (seed == Math.Floor(seed) && seed is >= int.MinValue and <= int.MaxValue)
            {
                _diagram.Seed = (int)seed;
            }
            else
            {
                _context.Warn(0, "The configuration 'cynefin.seed' must be an integer: it is ignored.");
            }
        }

        var theme = _diagram.Colors;
        ReadColor("complexBg", color => theme.ComplexBackground = color);
        ReadColor("complicatedBg", color => theme.ComplicatedBackground = color);
        ReadColor("clearBg", color => theme.ClearBackground = color);
        ReadColor("chaoticBg", color => theme.ChaoticBackground = color);
        ReadColor("confusionBg", color => theme.ConfusionBackground = color);
        ReadColor("boundaryColor", color => theme.BoundaryColor = color);
        ReadColor("cliffColor", color => theme.CliffColor = color);
        ReadColor("arrowColor", color => theme.ArrowColor = color);
        ReadColor("labelColor", color => theme.LabelColor = color);
        ReadColor("textColor", color => theme.TextColor = color);
        ReadSize("boundaryWidth", 0, 100, value => theme.BoundaryWidth = value);
        ReadSize("cliffWidth", 0, 100, value => theme.CliffWidth = value);
        ReadSize("arrowWidth", 0, 100, value => theme.ArrowWidth = value);
        ReadSize("domainFontSize", 1, 1_000, value => theme.DomainFontSize = value);
        ReadSize("itemFontSize", 2, 1_000, value => theme.ItemFontSize = value);

        void ReadNumber(string name, double minimum, double maximum, Action<double> set)
        {
            if (configuration.GetNumber("cynefin", name) is not { } value)
                return;

            if (value >= minimum && value <= maximum)
            {
                set(value);
            }
            else
            {
                _context.Warn(0, $"The configuration 'cynefin.{name}' must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}: it is ignored.");
            }
        }

        void ReadColor(string name, Action<SceneColor> set)
        {
            if (configuration.GetString("themeVariables", "cynefin", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
            {
                set(color);
            }
        }

        void ReadSize(string name, double minimum, double maximum, Action<double> set)
        {
            // Mermaid ignores 0 and uses the default (cynefin?.boundaryWidth || 2)
            if (configuration.GetNumber("themeVariables", "cynefin", name) is { } value && value >= minimum && value <= maximum && value != 0)
            {
                set(value);
            }
        }
    }
}
