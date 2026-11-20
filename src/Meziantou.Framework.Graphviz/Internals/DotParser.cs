using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>Reads the statements of a DOT graph: the attribute defaults of each scope, the nodes, the edges and the subgraphs.</summary>
internal sealed class DotParser
{
    private readonly DotContext _context;
    private readonly DotLexer _lexer;
    private readonly Dictionary<string, DotSubgraph> _subgraphsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(DotNode Tail, DotNode Head), DotEdge> _strictEdges = [];
    private DotGraph _graph = null!;
    private int _anonymousSubgraphs;
    private int _edgeCount;
    private long _inheritedAttributeCount;
    private int _depth;

    private DotParser(DotContext context)
    {
        _context = context;
        _lexer = new DotLexer(context);
    }

    public static DotGraph Parse(DotContext context) => new DotParser(context).ParseGraph();

    private DotGraph ParseGraph()
    {
        var token = _lexer.Next();
        if (token.Kind is DotTokenKind.End)
            throw _context.Error(token.Start, "The text has no graph.");

        var isStrict = false;
        if (IsKeyword(token, "strict"))
        {
            isStrict = true;
            token = _lexer.Next();
        }

        bool isDirected;
        if (IsKeyword(token, "digraph"))
        {
            isDirected = true;
        }
        else if (IsKeyword(token, "graph"))
        {
            isDirected = false;
        }
        else
        {
            throw _context.Error(token.Start, $"Expected 'graph' or 'digraph', but found {Describe(token)}.");
        }

        string? name = null;
        if (IsIdentifierStart(_lexer.Peek()))
        {
            name = ParseId().Value;
        }

        Expect(DotTokenKind.LeftBrace, "'{'");
        var root = new DotSubgraph(name: null, parent: null, token.Start, new DotAttributes());
        _graph = new DotGraph(isStrict, isDirected, name, root);
        ParseStatements(new Scope(root, new DotAttributes(), new DotAttributes()));
        Expect(DotTokenKind.RightBrace, "'}'");
        var next = _lexer.Peek();
        if (next.Kind is not DotTokenKind.End)
        {
            _context.Warn(next.Start, "Only the first graph of the text is read.");
        }

        return _graph;
    }

    private void ParseStatements(Scope scope)
    {
        while (true)
        {
            var token = _lexer.Peek();
            if (token.Kind is DotTokenKind.RightBrace)
                return;

            if (token.Kind is DotTokenKind.End)
                throw _context.Error(token.Start, "The graph is not closed: '}' is missing.");

            ParseStatement(scope);
            if (_lexer.Peek().Kind is DotTokenKind.Semicolon)
            {
                _lexer.Next();
            }
        }
    }

    private void ParseStatement(Scope scope)
    {
        var token = _lexer.Peek();
        if (IsKeyword(token, "graph") || IsKeyword(token, "node") || IsKeyword(token, "edge"))
        {
            _lexer.Next();
            if (_lexer.Peek().Kind is not DotTokenKind.LeftBracket)
                throw _context.Error(_lexer.Peek().Start, $"Expected '[' after '{token.Value}', but found {Describe(_lexer.Peek())}.");

            var attributes = ParseAttributeLists();
            if (IsKeyword(token, "graph"))
            {
                scope.Subgraph.Attributes.SetAll(attributes);
            }
            else if (IsKeyword(token, "node"))
            {
                scope.NodeDefaults.SetAll(attributes);
            }
            else
            {
                scope.EdgeDefaults.SetAll(attributes);
            }

            return;
        }

        if (IsKeyword(token, "subgraph") || token.Kind is DotTokenKind.LeftBrace)
        {
            var subgraph = ParseSubgraph(scope);
            if (IsEdgeOperator(_lexer.Peek()))
            {
                ParseEdges(scope, new Operand(null, subgraph), token.Start);
            }

            return;
        }

        var id = ParseId();
        if (_lexer.Peek().Kind is DotTokenKind.Equal)
        {
            _lexer.Next();
            var value = ParseId();
            scope.Subgraph.Attributes.Set(id.Value, new DotAttribute(value.Value, value.Kind is DotTokenKind.HtmlString, id.Start));
            return;
        }

        ParsePort();
        var node = Mention(id, scope);
        if (IsEdgeOperator(_lexer.Peek()))
        {
            ParseEdges(scope, new Operand(node, null), id.Start);
            return;
        }

        if (_lexer.Peek().Kind is DotTokenKind.LeftBracket)
        {
            node.Attributes.SetAll(ParseAttributeLists());
        }
    }

    private DotSubgraph ParseSubgraph(Scope scope)
    {
        var start = _lexer.Peek().Start;
        string? name = null;
        if (IsKeyword(_lexer.Peek(), "subgraph"))
        {
            _lexer.Next();
            if (IsIdentifierStart(_lexer.Peek()))
            {
                name = ParseId().Value;
            }

            if (_lexer.Peek().Kind is not DotTokenKind.LeftBrace)
            {
                // "subgraph name" without a body refers to the subgraph
                if (name is null)
                    throw _context.Error(_lexer.Peek().Start, $"Expected '{{' or the name of the subgraph, but found {Describe(_lexer.Peek())}.");

                return GetOrCreateSubgraph(name, scope.Subgraph, start);
            }
        }

        // The bodies are counted as they are opened: a named subgraph reopened inside itself keeps its depth
        if (_depth + 1 > _context.Options.MaxDepth)
            throw _context.Error(start, string.Create(CultureInfo.InvariantCulture, $"The subgraphs are nested too deeply: the limit is {_context.Options.MaxDepth}."));

        Expect(DotTokenKind.LeftBrace, "'{'");
        var subgraph = GetOrCreateSubgraph(name, scope.Subgraph, start);

        // A subgraph starts with the defaults of the enclosing scope, and keeps its own when its body is reopened
        subgraph.NodeDefaults ??= CloneDefaults(scope.NodeDefaults, start);
        subgraph.EdgeDefaults ??= CloneDefaults(scope.EdgeDefaults, start);
        _depth++;
        ParseStatements(new Scope(subgraph, subgraph.NodeDefaults, subgraph.EdgeDefaults));
        _depth--;
        Expect(DotTokenKind.RightBrace, "'}'");
        return subgraph;
    }

    private DotAttributes CloneDefaults(DotAttributes attributes, int position)
    {
        CountInheritedAttributes(attributes.Count, position);
        return attributes.Clone();
    }

    private void CountInheritedAttributes(int count, int position)
    {
        // The defaults are copied to each node, edge and subgraph: a long list of defaults and many elements would multiply the memory used
        _inheritedAttributeCount += count;
        if (_inheritedAttributeCount > _context.Options.MaxInheritedAttributes)
            throw _context.Error(position, string.Create(CultureInfo.InvariantCulture, $"The default attributes are copied to too many nodes, edges and subgraphs: the limit is {_context.Options.MaxInheritedAttributes} attributes."));
    }

    private void ParseEdges(Scope scope, Operand first, int start)
    {
        var operands = new List<Operand> { first };
        while (IsEdgeOperator(_lexer.Peek()))
        {
            var edgeOperator = _lexer.Next();
            if (_graph.IsDirected && edgeOperator.Kind is DotTokenKind.UndirectedEdge)
                throw _context.Error(edgeOperator.Start, "The edges of a directed graph (digraph) are written '->', not '--'.");

            if (!_graph.IsDirected && edgeOperator.Kind is DotTokenKind.DirectedEdge)
                throw _context.Error(edgeOperator.Start, "The edges of an undirected graph (graph) are written '--', not '->'.");

            var token = _lexer.Peek();
            if (IsKeyword(token, "subgraph") || token.Kind is DotTokenKind.LeftBrace)
            {
                operands.Add(new Operand(null, ParseSubgraph(scope)));
            }
            else
            {
                var id = ParseId();
                ParsePort();
                operands.Add(new Operand(Mention(id, scope), null));
            }
        }

        var attributes = _lexer.Peek().Kind is DotTokenKind.LeftBracket ? ParseAttributeLists() : new DotAttributes();
        for (var i = 0; i + 1 < operands.Count; i++)
        {
            var tails = operands[i].GetNodes();
            var heads = operands[i + 1].GetNodes();
            foreach (var tail in tails)
            {
                foreach (var head in heads)
                {
                    AddEdge(tail, head, scope.EdgeDefaults, attributes, start);
                }
            }
        }
    }

    private void AddEdge(DotNode tail, DotNode head, DotAttributes defaults, DotAttributes attributes, int position)
    {
        // The edges merged in a strict graph count too, so that expanding large groups of nodes stays bounded
        if (++_edgeCount > _context.Options.MaxEdges)
            throw _context.Error(position, string.Create(CultureInfo.InvariantCulture, $"The graph has too many edges: the limit is {_context.Options.MaxEdges}."));

        if (_graph.IsStrict)
        {
            // A strict graph has at most one edge between two nodes: the explicit attributes of the next ones are merged, not their defaults
            if (_strictEdges.TryGetValue((tail, head), out var existing) || (!_graph.IsDirected && _strictEdges.TryGetValue((head, tail), out existing)))
            {
                existing.Attributes.SetAll(attributes);
                return;
            }
        }

        var edgeAttributes = CloneDefaults(defaults, position);
        edgeAttributes.SetAll(attributes);
        var edge = new DotEdge(tail, head, position, edgeAttributes);
        _graph.Edges.Add(edge);
        if (_graph.IsStrict)
        {
            _strictEdges[(tail, head)] = edge;
        }
    }

    private DotAttributes ParseAttributeLists()
    {
        var attributes = new DotAttributes();
        while (_lexer.Peek().Kind is DotTokenKind.LeftBracket)
        {
            _lexer.Next();
            while (true)
            {
                if (_lexer.Peek().Kind is DotTokenKind.RightBracket)
                {
                    _lexer.Next();
                    break;
                }

                var name = ParseId();
                if (_lexer.Peek().Kind is not DotTokenKind.Equal)
                    throw _context.Error(_lexer.Peek().Start, $"Expected '=' after the attribute '{name.Value}', but found {Describe(_lexer.Peek())}.");

                _lexer.Next();
                var value = ParseId();
                attributes.Set(name.Value, new DotAttribute(value.Value, value.Kind is DotTokenKind.HtmlString, name.Start));
                if (_lexer.Peek().Kind is DotTokenKind.Semicolon or DotTokenKind.Comma)
                {
                    _lexer.Next();
                }
            }
        }

        return attributes;
    }

    private void ParsePort()
    {
        if (_lexer.Peek().Kind is not DotTokenKind.Colon)
            return;

        var start = _lexer.Next().Start;
        ParseId();
        if (_lexer.Peek().Kind is DotTokenKind.Colon)
        {
            _lexer.Next();
            ParseId();
        }

        _context.WarnOnce("port", start, "The ports of the nodes (node:port:compass) are not supported: the edges are attached to the nodes.");
    }

    private DotToken ParseId()
    {
        var token = _lexer.Next();
        switch (token.Kind)
        {
            case DotTokenKind.Identifier when IsKeyword(token):
                throw _context.Error(token.Start, $"'{token.Value}' is a keyword: quote it to use it as a name.");
            case DotTokenKind.Identifier or DotTokenKind.Numeral or DotTokenKind.HtmlString:
                return token;
            case DotTokenKind.QuotedString:
                if (_lexer.Peek().Kind is not DotTokenKind.Plus)
                    return token;

                var value = new StringBuilder(token.Value);
                while (_lexer.Peek().Kind is DotTokenKind.Plus)
                {
                    _lexer.Next();
                    var next = _lexer.Next();
                    if (next.Kind is not DotTokenKind.QuotedString)
                        throw _context.Error(next.Start, $"Only quoted strings can be joined with '+', but found {Describe(next)}.");

                    value.Append(next.Value);
                }

                return token with { Value = value.ToString() };
            default:
                throw _context.Error(token.Start, $"Expected a name, a number or a string, but found {Describe(token)}.");
        }
    }

    private void Expect(DotTokenKind kind, string description)
    {
        var token = _lexer.Next();
        if (token.Kind != kind)
            throw _context.Error(token.Start, $"Expected {description}, but found {Describe(token)}.");
    }

    private DotNode Mention(DotToken id, Scope scope)
    {
        if (id.Value.Length is 0)
            throw _context.Error(id.Start, "The name of a node cannot be empty.");

        if (!_graph.NodesById.TryGetValue(id.Value, out var node))
        {
            if (_graph.Nodes.Count >= _context.Options.MaxNodes)
                throw _context.Error(id.Start, string.Create(CultureInfo.InvariantCulture, $"The graph has too many nodes: the limit is {_context.Options.MaxNodes}."));

            node = new DotNode(id.Value, id.Start, CloneDefaults(scope.NodeDefaults, id.Start));
            _graph.Nodes.Add(node);
            _graph.NodesById.Add(node.Id, node);
        }

        var subgraph = scope.Subgraph;
        if (subgraph.Parent is not null && !node.Mentions.Contains(subgraph))
        {
            node.Mentions.Add(subgraph);
        }

        for (var current = subgraph; current is not null; current = current.Parent)
        {
            if (!current.AddNode(node))
                break;
        }

        return node;
    }

    private DotSubgraph GetOrCreateSubgraph(string? name, DotSubgraph parent, int position)
    {
        if (name is { Length: 0 })
        {
            name = null;
        }

        if (name is not null && _subgraphsByName.TryGetValue(name, out var existing))
            return existing;

        // A subgraph starts with the attributes of its parent graph, as in Graphviz
        CountInheritedAttributes(parent.Attributes.Count, position);
        var subgraph = new DotSubgraph(name, parent, position, parent.Attributes.CloneInherited())
        {
            GeneratedName = name is null ? "%" + (++_anonymousSubgraphs).ToString(CultureInfo.InvariantCulture) : null,
        };

        parent.Children.Add(subgraph);
        _graph.Subgraphs.Add(subgraph);
        if (name is not null)
        {
            _subgraphsByName.Add(name, subgraph);
        }

        return subgraph;
    }

    private static bool IsEdgeOperator(DotToken token) => token.Kind is DotTokenKind.DirectedEdge or DotTokenKind.UndirectedEdge;

    private static bool IsIdentifierStart(DotToken token) => token.Kind switch
    {
        DotTokenKind.Identifier => !IsKeyword(token),
        DotTokenKind.Numeral or DotTokenKind.QuotedString or DotTokenKind.HtmlString => true,
        _ => false,
    };

    internal static bool IsKeyword(string value) => value.Equals("node", StringComparison.OrdinalIgnoreCase)
        || value.Equals("edge", StringComparison.OrdinalIgnoreCase)
        || value.Equals("graph", StringComparison.OrdinalIgnoreCase)
        || value.Equals("digraph", StringComparison.OrdinalIgnoreCase)
        || value.Equals("subgraph", StringComparison.OrdinalIgnoreCase)
        || value.Equals("strict", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeyword(DotToken token) => token.Kind is DotTokenKind.Identifier && IsKeyword(token.Value);

    private static bool IsKeyword(DotToken token, string keyword) => token.Kind is DotTokenKind.Identifier && token.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    private static string Describe(DotToken token) => token.Kind switch
    {
        DotTokenKind.End => "the end of the text",
        DotTokenKind.QuotedString => "a string",
        DotTokenKind.HtmlString => "an HTML string",
        _ => "'" + token.Value + "'",
    };

    private sealed record Scope(DotSubgraph Subgraph, DotAttributes NodeDefaults, DotAttributes EdgeDefaults);

    private readonly record struct Operand(DotNode? Node, DotSubgraph? Subgraph)
    {
        public IReadOnlyList<DotNode> GetNodes() => Node is not null ? [Node] : [.. Subgraph!.Nodes];
    }
}
