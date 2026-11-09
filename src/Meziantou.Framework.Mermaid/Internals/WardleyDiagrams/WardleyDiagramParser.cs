using System.Globalization;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.WardleyDiagrams;

/// <summary>
/// Parses the <c>wardley-beta</c> diagrams of Mermaid, as its Langium grammar reads them: a statement per line, the
/// coordinates as <c>[visibility, evolution]</c> between 0 and 1, or between 1 and 100 as percentages.
/// </summary>
internal sealed class WardleyDiagramParser
{
    private const string Section = "wardley-beta";
    private const int MaxSize = 100_000;

    private readonly ParseContext _context;
    private readonly string _text;
    private readonly WardleyDiagram _diagram = new();
    private readonly List<(WardleyPipeline Pipeline, int Position)> _pipelines = [];
    private readonly List<(string Component, double Value, int Position)> _evolves = [];
    private int _position;
    private int _nodes;
    private int _edges;
    private bool _hasAnnotationsBox;
    private HashSet<string>? _nodeNames;

    private WardleyDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _text = context.Text;
        _position = position;
    }

    public static WardleyDiagram Parse(ParseContext context, int position) => new WardleyDiagramParser(context, position).Parse();

    private WardleyDiagram Parse()
    {
        Expect(WardleyTokenKind.KeywordWardley, "'wardley-beta' is expected.");
        ApplyConfiguration();
        while (true)
        {
            var token = Peek();
            if (token.Kind is WardleyTokenKind.End)
                break;

            if (token.Kind is WardleyTokenKind.Newline)
            {
                _position = token.End;
                continue;
            }

            ParseStatement(token);
        }

        ResolvePipelines();
        ResolveEvolves();
        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, Section);
        SetNumber("width", 0, MaxSize, value => _diagram.Width = value);
        SetNumber("height", 0, MaxSize, value => _diagram.Height = value);
        SetNumber("padding", 0, 10_000, value => _diagram.MapPadding = value);
        SetNumber("nodeRadius", 0, 1000, value => _diagram.NodeRadius = value);
        SetNumber("nodeLabelOffset", 0, 1000, value => _diagram.NodeLabelOffset = value);
        SetNumber("axisFontSize", 1, 1000, value => _diagram.AxisFontSize = value);
        SetNumber("labelFontSize", 1, 1000, value => _diagram.LabelFontSize = value);
        if (_context.Configuration.GetBoolean(Section, "showGrid") is { } showGrid)
        {
            _diagram.ShowGrid = showGrid;
        }
    }

    private void SetNumber(string name, double minimum, double maximum, Action<double> setter)
    {
        if (_context.Configuration.GetNumber(Section, name) is not { } value)
            return;

        if (value >= minimum && value <= maximum)
        {
            setter(value);
        }
        else
        {
            _context.Warn(0, string.Create(CultureInfo.InvariantCulture, $"The configuration '{Section}.{name}' must be between {minimum} and {maximum}: it is ignored."));
        }
    }

    private void ParseStatement(WardleyToken token)
    {
        switch (token.Kind)
        {
            case WardleyTokenKind.Title:
                {
                    var scanner = new Scanner(_context, token.Start);
                    if (!GitGraphLangium.TryParseTitle(scanner, _diagram))
                        throw _context.Error(token.End, $"Unexpected '{Describe(Next(token.End))}'.");

                    _position = scanner.Position;
                    break;
                }

            case WardleyTokenKind.AccessibleTitle or WardleyTokenKind.AccessibleDescription:
                {
                    var scanner = new Scanner(_context, token.Start);
                    if (MermaidCommon.TryParseAccessibility(scanner, _diagram))
                    {
                        _position = scanner.Position;
                    }
                    else
                    {
                        // As Mermaid, the block of the description can start on the next line
                        ParseDescriptionBlock(token.Start);
                    }

                    break;
                }

            case WardleyTokenKind.KeywordSize:
                _position = token.End;
                Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
                var width = ReadInteger(MaxSize, "width");
                Expect(WardleyTokenKind.Comma, "',' is expected.");
                var height = ReadInteger(MaxSize, "height");
                Expect(WardleyTokenKind.RightBracket, "']' is expected.");
                _diagram.Width = width;
                _diagram.Height = height;
                break;

            case WardleyTokenKind.KeywordEvolution:
                _position = token.End;
                ParseEvolution();
                break;

            case WardleyTokenKind.KeywordAnchor:
                {
                    _position = token.End;
                    var name = ReadName();
                    var (visibility, evolution) = ReadCoordinates("Anchor", name);
                    AddNode(token.Start);
                    _diagram.AddAnchor(name, visibility, evolution);
                    break;
                }

            case WardleyTokenKind.KeywordComponent:
                _position = token.End;
                ParseComponent(token.Start);
                break;

            case WardleyTokenKind.KeywordEvolve:
                {
                    _position = token.End;
                    var name = ReadName();
                    var valueToken = Expect(WardleyTokenKind.Number, "A decimal number, such as 0.5, is expected.");
                    AddEdge(token.Start);
                    _evolves.Add((name, ParseDecimal(valueToken), valueToken.Start));
                    break;
                }

            case WardleyTokenKind.KeywordPipeline:
                _position = token.End;
                ParsePipeline(token.Start);
                break;

            case WardleyTokenKind.KeywordNote:
                {
                    _position = token.End;
                    var text = ReadQuotedText("The text of the note must be quoted.");
                    var (visibility, evolution) = ReadCoordinates("Note", text);
                    AddNode(token.Start);
                    _diagram.AddNote(text, visibility, evolution);
                    break;
                }

            case WardleyTokenKind.KeywordAnnotations:
                ParseAnnotationsBox(token);
                break;

            case WardleyTokenKind.KeywordAnnotation:
                _position = token.End;
                ParseAnnotation(token.Start);
                break;

            case WardleyTokenKind.KeywordAccelerator or WardleyTokenKind.KeywordDeaccelerator:
                {
                    _position = token.End;
                    var accelerator = token.Kind is WardleyTokenKind.KeywordAccelerator;
                    var name = ReadName();
                    var (visibility, evolution) = ReadCoordinates(accelerator ? "Accelerator" : "Deaccelerator", name);
                    AddNode(token.Start);
                    (accelerator ? _diagram.Accelerators : _diagram.Deaccelerators).Add(new WardleyAccelerator(name, visibility, evolution));
                    break;
                }

            case WardleyTokenKind.String or WardleyTokenKind.Identifier or WardleyTokenKind.Name:
                ParseLink(token);
                break;

            default:
                throw _context.Error(token.Start, $"Unexpected '{Describe(token)}': a statement is expected.");
        }

        ExpectLineEnd();
    }

    /// <summary>Reads <c>accDescr</c>, white space and line feeds, then a block of lines between braces.</summary>
    private void ParseDescriptionBlock(int start)
    {
        var open = _text.IndexOfOrdinal('{', start);
        var close = _text.IndexOfOrdinal('}', open);
        var lines = _text[(open + 1)..close].Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith("%%", StringComparison.Ordinal));
        _diagram.Description = string.Join('\n', lines).Trim();
        _position = close + 1;
    }

    private void ParseEvolution()
    {
        var stages = new List<WardleyStage> { ParseStage() };
        while (Peek().Kind is WardleyTokenKind.Arrow)
        {
            _position = Peek().End;
            stages.Add(ParseStage());
        }

        if (stages.Count < 2)
            throw _context.Error(Peek().Start, "The evolution needs at least two stages, separated by '->'.");

        if (stages.Count > _context.Options.MaxNodes)
            throw _context.Error(Peek().Start, $"The evolution has more than {_context.Options.MaxNodes} stages.");

        // The last evolution statement replaces the previous ones
        _diagram.Stages.Clear();
        foreach (var stage in stages)
        {
            _diagram.Stages.Add(stage);
        }
    }

    private WardleyStage ParseStage()
    {
        var name = ReadName();
        double? boundary = null;
        if (Peek().Kind is WardleyTokenKind.At)
        {
            _position = Peek().End;
            var boundaryToken = Expect(WardleyTokenKind.Number, "A decimal number, such as 0.25, is expected.");
            boundary = ParseDecimal(boundaryToken);
            if (!double.IsFinite(boundary.Value))
                throw _context.Error(boundaryToken.Start, "The boundary of the stage is too large.");
        }

        string? secondName = null;
        if (Peek().Kind is WardleyTokenKind.Slash)
        {
            _position = Peek().End;
            secondName = ReadName();
        }

        return new WardleyStage(name, secondName) { Boundary = boundary };
    }

    private void ParseComponent(int start)
    {
        var name = ReadName();
        var (visibility, evolution) = ReadCoordinates("Component", name);
        var component = new WardleyComponent(name, visibility, evolution) { LabelOffset = ParseLabelOffset() };
        if (Peek().Kind is WardleyTokenKind.LeftParenthesis && Next(Peek().End).Kind is WardleyTokenKind.Strategy)
        {
            _position = Peek().End;
            var strategy = Expect(WardleyTokenKind.Strategy, "A strategy is expected.");
            component.Sourcing = _text[strategy.Start..strategy.End] switch
            {
                "build" => WardleySourcing.Build,
                "buy" => WardleySourcing.Buy,
                "outsource" => WardleySourcing.Outsource,
                _ => WardleySourcing.Market,
            };
            Expect(WardleyTokenKind.RightParenthesis, "')' is expected.");
        }

        var token = Peek();
        if (token.Kind is WardleyTokenKind.KeywordInertia)
        {
            _position = token.End;
            component.Inertia = true;
        }
        else if (token.Kind is WardleyTokenKind.LeftParenthesis)
        {
            _position = token.End;
            Expect(WardleyTokenKind.KeywordInertia, "'inertia' or a strategy (build, buy, outsource, market) is expected.");
            Expect(WardleyTokenKind.RightParenthesis, "')' is expected.");
            component.Inertia = true;
        }

        AddNode(start);
        _diagram.Components.Add(component);
    }

    private WardleyLabelOffset? ParseLabelOffset()
    {
        if (Peek().Kind is not WardleyTokenKind.KeywordLabel)
            return null;

        _position = Peek().End;
        Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
        var x = ReadOffset();
        Expect(WardleyTokenKind.Comma, "',' is expected.");
        var y = ReadOffset();
        Expect(WardleyTokenKind.RightBracket, "']' is expected.");
        return new WardleyLabelOffset(x, y);
    }

    private int ReadOffset()
    {
        var negative = false;
        if (Peek().Kind is WardleyTokenKind.Minus)
        {
            _position = Peek().End;
            negative = true;
        }

        var token = Expect(WardleyTokenKind.Integer, "An integer is expected.");
        if (!long.TryParse(_text.AsSpan(token.Start, token.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > (long)int.MaxValue + 1 || (!negative && value > int.MaxValue))
            throw _context.Error(token.Start, "The offset of the label is too large.");

        return (int)(negative ? -value : value);
    }

    private void ParsePipeline(int start)
    {
        var parent = ReadName();
        Expect(WardleyTokenKind.LeftBrace, "'{' is expected.");
        if (Peek().Kind is not WardleyTokenKind.Newline)
            throw _context.Error(Peek().Start, "The components of a pipeline start on the next line.");

        var pipeline = new WardleyPipeline(parent);
        while (true)
        {
            var token = Peek();
            if (token.Kind is WardleyTokenKind.Newline)
            {
                _position = token.End;
                continue;
            }

            if (token.Kind is WardleyTokenKind.RightBrace && pipeline.Components.Count > 0)
            {
                _position = token.End;
                break;
            }

            if (token.Kind is not WardleyTokenKind.KeywordComponent)
                throw _context.Error(token.Start, pipeline.Components.Count > 0 ? $"Unexpected '{Describe(token)}': a component or '}}' is expected." : $"Unexpected '{Describe(token)}': a pipeline needs at least one component.");

            _position = token.End;
            var name = ReadName();
            Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
            var evolutionToken = Expect(WardleyTokenKind.Number, "A decimal number, such as 0.5, is expected.");
            var evolution = ToFraction(ParseDecimal(evolutionToken), $"Pipeline component \"{name}\" evolution", evolutionToken.Start);
            Expect(WardleyTokenKind.RightBracket, "']' is expected.");
            var component = new WardleyPipelineComponent(name, evolution) { LabelOffset = ParseLabelOffset() };
            var end = Peek();
            if (end.Kind is not WardleyTokenKind.Newline)
                throw _context.Error(end.Start, end.Kind is WardleyTokenKind.End ? "The pipeline is not closed by '}'." : $"Unexpected '{Describe(end)}'.");

            AddNode(token.Start);
            pipeline.Components.Add(component);
        }

        _diagram.Pipelines.Add(pipeline);
        _pipelines.Add((pipeline, start));
    }

    private void ParseAnnotationsBox(WardleyToken token)
    {
        _position = token.End;
        Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
        var visibility = ReadCoordinateValue();
        Expect(WardleyTokenKind.Comma, "',' is expected.");
        var evolution = ReadCoordinateValue();
        Expect(WardleyTokenKind.RightBracket, "']' is expected.");

        // Mermaid uses the first box, and ignores the others
        if (_hasAnnotationsBox)
        {
            _context.Warn(token.Start, "The annotations box is already placed: this position is ignored.");
            return;
        }

        _hasAnnotationsBox = true;
        _diagram.AnnotationsPosition = new WardleyPosition(ToFraction(visibility.Value, "Annotations box visibility", visibility.Position), ToFraction(evolution.Value, "Annotations box evolution", evolution.Position));
    }

    private void ParseAnnotation(int start)
    {
        var numberToken = Expect(WardleyTokenKind.Integer, "The number of the annotation is expected.");
        if (!int.TryParse(_text.AsSpan(numberToken.Start, numberToken.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw _context.Error(numberToken.Start, "The number of the annotation is too large.");

        Expect(WardleyTokenKind.Comma, "',' is expected.");
        Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
        var visibility = ReadCoordinateValue();
        Expect(WardleyTokenKind.Comma, "',' is expected.");
        var evolution = ReadCoordinateValue();
        Expect(WardleyTokenKind.RightBracket, "']' is expected.");
        var text = ReadQuotedText("The text of the annotation must be quoted.");
        var context = "Annotation " + number.ToString(CultureInfo.InvariantCulture);
        AddNode(start);
        _diagram.Annotations.Add(new WardleyAnnotation(number, text, ToFraction(visibility.Value, context + " visibility", visibility.Position), ToFraction(evolution.Value, context + " evolution", evolution.Position)));
    }

    private (double Value, int Position) ReadCoordinateValue()
    {
        var token = Peek();
        if (token.Kind is WardleyTokenKind.Number)
        {
            _position = token.End;
            return (ParseDecimal(token), token.Start);
        }

        if (token.Kind is WardleyTokenKind.Integer)
        {
            _position = token.End;
            return (double.Parse(_text.AsSpan(token.Start, token.Length), NumberStyles.None, CultureInfo.InvariantCulture), token.Start);
        }

        throw _context.Error(token.Start, $"Unexpected '{Describe(token)}': a number is expected.");
    }

    private void ParseLink(WardleyToken first)
    {
        var source = ReadName();
        WardleyFlow? flow = null;
        var token = Peek();
        if (token.Kind is WardleyTokenKind.LinkPort)
        {
            flow = GetPortFlow(token);
            _position = token.End;
            token = Peek();
        }

        var dashed = false;
        string? flowLabel = null;
        WardleyFlow? arrowFlow = null;
        if (token.Kind is WardleyTokenKind.LinkArrow or WardleyTokenKind.Arrow)
        {
            var arrow = _text[token.Start..token.End];
            dashed = arrow.Contains("-.->", StringComparison.Ordinal) || arrow.Contains(".-.", StringComparison.Ordinal);
            if (arrow[0] == '+')
            {
                // +'label'> gives the label and the direction of the flow
                var close = arrow.AsSpan().LastIndexOf('\'');
                flowLabel = WardleyEntities.Decode(arrow[2..close]);
                var direction = arrow[(close + 1)..];
                arrowFlow = direction switch
                {
                    "<>" => WardleyFlow.Bidirectional,
                    "<" => WardleyFlow.Backward,
                    _ => WardleyFlow.Forward,
                };
            }

            _position = token.End;
        }

        var target = ReadName();
        token = Peek();
        if (token.Kind is WardleyTokenKind.LinkPort)
        {
            flow ??= GetPortFlow(token);
            _position = token.End;
            token = Peek();
        }

        string? label = null;
        if (token.Kind is WardleyTokenKind.LinkLabel)
        {
            label = WardleyEntities.Decode(WardleyEntities.Trim(_text[(token.Start + 1)..token.End]));
            _position = token.End;
        }

        AddEdge(first.Start);
        var link = _diagram.AddLink(source, target);
        link.IsDashed = dashed;
        link.Flow = flow ?? arrowFlow ?? WardleyFlow.None;
        label = flowLabel ?? label;
        link.Label = string.IsNullOrEmpty(label) ? null : label;
    }

    private WardleyFlow GetPortFlow(WardleyToken token) => _text[token.Start..token.End] switch
    {
        "+<>" => WardleyFlow.Bidirectional,
        "+<" => WardleyFlow.Backward,
        _ => WardleyFlow.Forward,
    };

    /// <summary>Checks that the parent of each pipeline is on the map, in the order Mermaid builds it: the anchors, the components, then the components of the previous pipelines.</summary>
    private void ResolvePipelines()
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var anchor in _diagram.Anchors)
        {
            nodes.Add(anchor.Name);
        }

        foreach (var component in _diagram.Components)
        {
            nodes.Add(component.Name);
        }

        foreach (var (pipeline, position) in _pipelines)
        {
            if (!nodes.Contains(pipeline.Parent))
                throw _context.Error(position, $"Pipeline \"{pipeline.Parent}\" must reference an existing component with coordinates.");

            foreach (var component in pipeline.Components)
            {
                nodes.Add(pipeline.Parent + "_" + component.Name);
            }
        }

        _nodeNames = nodes;
    }

    /// <summary>Adds the movements: Mermaid checks the target of a movement of a component on the map, and ignores the others.</summary>
    private void ResolveEvolves()
    {
        foreach (var (component, value, position) in _evolves)
        {
            if (_nodeNames!.Contains(component))
            {
                _diagram.AddEvolve(component, ToFraction(value, $"Evolve target for \"{component}\"", position));
            }
            else if (value <= 100)
            {
                _diagram.AddEvolve(component, value <= 1 ? value : value / 100);
            }
            else
            {
                _context.Warn(position, $"The component \"{component}\" is not on the map: its movement is ignored.");
            }
        }
    }

    private (double Visibility, double Evolution) ReadCoordinates(string kind, string name)
    {
        Expect(WardleyTokenKind.LeftBracket, "'[' is expected.");
        var visibilityToken = Expect(WardleyTokenKind.Number, "A decimal number, such as 0.5, is expected.");
        Expect(WardleyTokenKind.Comma, "',' is expected.");
        var evolutionToken = Expect(WardleyTokenKind.Number, "A decimal number, such as 0.5, is expected.");
        Expect(WardleyTokenKind.RightBracket, "']' is expected.");
        var context = $"{kind} \"{name}\"";
        return (ToFraction(ParseDecimal(visibilityToken), context + " visibility", visibilityToken.Start), ToFraction(ParseDecimal(evolutionToken), context + " evolution", evolutionToken.Start));
    }

    /// <summary>Converts a coordinate as Mermaid: a value up to 1 is a fraction, a value up to 100 a percentage.</summary>
    private double ToFraction(double value, string context, int position)
    {
        if (value <= 1)
            return value;

        if (value <= 100)
            return value / 100;

        throw _context.Error(position, string.Create(CultureInfo.InvariantCulture, $"{context} must be between 0-1 (decimal) or 0-100 (percentage). Received: {value}"));
    }

    private double ParseDecimal(WardleyToken token) => double.Parse(_text.AsSpan(token.Start, token.Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private int ReadInteger(int maximum, string name)
    {
        var token = Expect(WardleyTokenKind.Integer, "An integer is expected.");
        if (!int.TryParse(_text.AsSpan(token.Start, token.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > maximum)
            throw _context.Error(token.Start, string.Create(CultureInfo.InvariantCulture, $"The {name} cannot be larger than {maximum}."));

        return value;
    }

    /// <summary>Reads a name: a quoted string, an identifier, or a name with spaces.</summary>
    private string ReadName()
    {
        var token = Peek();
        _position = token.End;
        return token.Kind switch
        {
            WardleyTokenKind.String => WardleyEntities.Decode(WardleyLexer.ReadString(_text, token)),
            WardleyTokenKind.Identifier or WardleyTokenKind.Name => _text[token.Start..token.End],
            _ => throw _context.Error(token.Start, $"Unexpected '{Describe(token)}': a name is expected."),
        };
    }

    private string ReadQuotedText(string message)
    {
        var token = Expect(WardleyTokenKind.String, message);
        return WardleyEntities.Decode(WardleyLexer.ReadString(_text, token));
    }

    private void AddNode(int position)
    {
        if (++_nodes > _context.Options.MaxNodes)
            throw _context.Error(position, $"The diagram has more than {_context.Options.MaxNodes} elements.");
    }

    private void AddEdge(int position)
    {
        if (++_edges > _context.Options.MaxEdges)
            throw _context.Error(position, $"The diagram has more than {_context.Options.MaxEdges} links and movements.");
    }

    private void ExpectLineEnd()
    {
        var token = Peek();
        if (token.Kind is not (WardleyTokenKind.Newline or WardleyTokenKind.End))
            throw _context.Error(token.Start, $"Unexpected '{Describe(token)}'.");
    }

    private WardleyToken Expect(WardleyTokenKind kind, string message)
    {
        var token = Peek();
        if (token.Kind != kind)
            throw _context.Error(token.Start, token.Kind is WardleyTokenKind.End or WardleyTokenKind.Newline ? message : $"Unexpected '{Describe(token)}': {char.ToLowerInvariant(message[0])}{message[1..]}");

        _position = token.End;
        return token;
    }

    private WardleyToken Peek() => Next(_position);

    private WardleyToken Next(int position) => WardleyLexer.Next(_text, position);

    private string Describe(WardleyToken token) => token.Kind switch
    {
        WardleyTokenKind.End => "end of text",
        WardleyTokenKind.Newline => "end of line",
        _ => _text[token.Start..Math.Min(token.End, token.Start + 20)],
    };
}
