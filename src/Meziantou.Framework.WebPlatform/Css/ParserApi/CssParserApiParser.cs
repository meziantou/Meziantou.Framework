using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.ParserApi;

/// <summary>
/// The CSS Parser API (https://wicg.github.io/css-parser-api/): the entry points of CSS Syntax
/// (https://drafts.csswg.org/css-syntax-3/#parser-entry-points), run by <see cref="CssParser"/>, whose rules, declarations
/// and component values are converted to the objects of the API. The draft defines no algorithm: each operation runs the
/// entry point of CSS Syntax of the same name, and the blocks of the rules are parsed with "consume a block's contents".
/// </summary>
internal sealed class CssParserApiParser
{
    /// <summary>The source text of the tokens (the preprocessed input), whose positions are relative to it.</summary>
    private readonly string _source;
    private readonly Dictionary<string, BlockGrammar>? _atRules;

    private CssParserApiParser(string source, Dictionary<string, BlockGrammar>? atRules)
    {
        _source = source;
        _atRules = atRules;
    }

    /// <summary>The productions of the blocks (https://drafts.csswg.org/css-syntax-3/#block-contents).</summary>
    private enum BlockGrammar
    {
        BlockContents,
        DeclarationList,
        QualifiedRuleList,
        AtRuleList,
        DeclarationRuleList,
        RuleList,
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-stylesheet (the bytes of a stream are decoded by WebIdlRealm.CssParserApi.cs)</summary>
    public static IReadOnlyList<CssParserRule> ParseStylesheet(string css, CssParserOptions? options)
    {
        var parser = Create(css, options, out var values);
        return parser.ConvertRules(CssParser.ConsumeStylesheetContents(new CssComponentValueStream(values), reportErrors: false), BlockGrammar.BlockContents);
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-stylesheet-contents (formerly "parse a list of rules")</summary>
    public static IReadOnlyList<CssParserRule> ParseRuleList(string css, CssParserOptions? options) => ParseStylesheet(css, options);

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-rule</summary>
    public static CssParserRule? ParseRule(string css, CssParserOptions? options)
    {
        var parser = Create(css, options, out var values);
        var input = new CssComponentValueStream(values);
        input.DiscardWhitespace();
        if (input.IsEmpty)
            return null;

        CssSyntaxNode? rule = input.NextIs(CssTokenType.AtKeyword) ? CssParser.ConsumeAtRule(input, nested: false) : CssParser.ConsumeQualifiedRule(input, stopToken: null, nested: false);
        if (rule is null)
            return null;

        input.DiscardWhitespace();
        return input.IsEmpty ? parser.ConvertRule(rule) : null;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-list-of-declarations: the at-rules are consumed and discarded.</summary>
    public static IReadOnlyList<CssParserRule> ParseDeclarationList(string css, CssParserOptions? options)
    {
        var parser = Create(css, options, out var values);
        var input = new CssComponentValueStream(values);
        var declarations = new List<CssParserRule>();
        while (!input.IsEmpty)
        {
            input.DiscardWhitespace();
            if (input.IsEmpty)
                break;

            if (input.NextIs(CssTokenType.Semicolon))
            {
                input.Discard();
            }
            else if (input.NextIs(CssTokenType.AtKeyword))
            {
                CssParser.ConsumeAtRule(input, nested: false);
            }
            else if (CssParser.ConsumeDeclaration(input, nested: false) is { } declaration)
            {
                declarations.Add(parser.ConvertDeclaration(declaration));
            }
        }

        return declarations.AsReadOnly();
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-declaration: null for a syntax error (e.g. a trailing semicolon).</summary>
    public static CssParserDeclaration? ParseDeclaration(string css, CssParserOptions? options)
    {
        var parser = Create(css, options, out var values);
        var input = new CssComponentValueStream(values);
        input.DiscardWhitespace();
        if (input.IsEmpty)
            return null;

        if (CssParser.ConsumeDeclaration(input, nested: false) is not { } declaration)
            return null;

        input.DiscardWhitespace();
        return input.IsEmpty ? parser.ConvertDeclaration(declaration) : null;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-component-value: null for a syntax error.</summary>
    public static CssParserToken? ParseValue(string css)
    {
        var parser = Create(css, options: null, out var values);
        var start = 0;
        var end = values.Count;
        while (start < end && IsWhitespace(values[start]))
        {
            start++;
        }

        while (end > start && IsWhitespace(values[end - 1]))
        {
            end--;
        }

        return end - start == 1 ? parser.ConvertValue(values[start]) : (CssParserToken?)null;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-list-of-component-values</summary>
    public static IReadOnlyList<CssParserToken> ParseValueList(string css)
    {
        var parser = Create(css, options: null, out var values);
        return parser.ConvertValues(values);
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-comma-separated-list-of-component-values</summary>
    public static IReadOnlyList<IReadOnlyList<CssParserToken>> ParseCommaValueList(string css)
    {
        var parser = Create(css, options: null, out var values);
        var groups = new List<IReadOnlyList<CssParserToken>>();
        var index = 0;
        while (index < values.Count)
        {
            var group = new List<CssParserToken>();
            while (index < values.Count && values[index] is not CssPreservedToken { Type: CssTokenType.Comma })
            {
                group.Add(parser.ConvertValue(values[index]));
                index++;
            }

            groups.Add(group.AsReadOnly());
            index++; // the comma
        }

        return groups.AsReadOnly();
    }

    private static CssParserApiParser Create(string css, CssParserOptions? options, out List<CssComponentValue> values)
    {
        ArgumentNullException.ThrowIfNull(css);
        var atRules = GetAtRules(options);
        values = CssParser.ParseListOfComponentValues(css, out var source);
        return new CssParserApiParser(source, atRules);
    }

    /// <summary>The grammar of the blocks of the at-rules (<see cref="CssParserOptions.AtRules"/>), by ASCII lowercase name.</summary>
    private static Dictionary<string, BlockGrammar>? GetAtRules(CssParserOptions? options)
    {
        if (options?.AtRules is not { } atRules)
            return null;

        var result = new Dictionary<string, BlockGrammar>(StringComparer.Ordinal);
        foreach (var (name, grammar) in atRules)
        {
            result[InfraAscii.ToAsciiLowercase(name)] = grammar switch
            {
                "block-contents" => BlockGrammar.BlockContents,
                "declaration-list" => BlockGrammar.DeclarationList,
                "qualified-rule-list" => BlockGrammar.QualifiedRuleList,
                "at-rule-list" => BlockGrammar.AtRuleList,
                "declaration-rule-list" => BlockGrammar.DeclarationRuleList,
                "rule-list" => BlockGrammar.RuleList,
                _ => throw new TypeErrorException($"'{grammar}' is not a block production: it must be \"block-contents\", \"declaration-list\", \"qualified-rule-list\", \"at-rule-list\", \"declaration-rule-list\" or \"rule-list\""),
            };
        }

        return result;
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<CssParserRule> ConvertRules(List<CssSyntaxNode> nodes, BlockGrammar grammar)
    {
        var rules = new List<CssParserRule>(nodes.Count);
        foreach (var node in nodes)
        {
            var allowed = node switch
            {
                CssDeclaration => grammar is BlockGrammar.BlockContents or BlockGrammar.DeclarationList or BlockGrammar.DeclarationRuleList,
                CssQualifiedRule => grammar is BlockGrammar.BlockContents or BlockGrammar.QualifiedRuleList or BlockGrammar.RuleList,
                CssAtRule => grammar is BlockGrammar.BlockContents or BlockGrammar.AtRuleList or BlockGrammar.DeclarationRuleList or BlockGrammar.RuleList,
                _ => false,
            };

            if (allowed && ConvertRule(node) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules.AsReadOnly();
    }

    private CssParserRule? ConvertRule(CssSyntaxNode node)
    {
        switch (node)
        {
            case CssAtRule atRule:
            {
                var grammar = _atRules is not null && _atRules.TryGetValue(InfraAscii.ToAsciiLowercase(atRule.Name), out var value) ? value : BlockGrammar.BlockContents;
                var body = atRule.Block is { } block ? ParseBlockContents(block, grammar) : null;
                return new CssParserAtRule(atRule.Name, ConvertValues(atRule.Prelude), body);
            }

            case CssQualifiedRule qualifiedRule:
                // Style rules use <block-contents> (https://drafts.csswg.org/css-syntax-3/#block-contents)
                return new CssParserQualifiedRule(ConvertValues(qualifiedRule.Prelude), ParseBlockContents(qualifiedRule.Block, BlockGrammar.BlockContents));

            case CssDeclaration declaration:
                return ConvertDeclaration(declaration);

            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-block-contents, filtered by the production of the block.</summary>
    private System.Collections.ObjectModel.ReadOnlyCollection<CssParserRule> ParseBlockContents(CssSimpleBlock block, BlockGrammar grammar)
        => ConvertRules(CssParser.ConsumeBlockContents(new CssComponentValueStream(block.Value)), grammar);

    private CssParserDeclaration ConvertDeclaration(CssDeclaration declaration) => new(declaration.Name, ConvertValues(declaration.Value)) { Important = declaration.Important };

    private System.Collections.ObjectModel.ReadOnlyCollection<CssParserToken> ConvertValues(List<CssComponentValue> values)
    {
        var result = new CssParserToken[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            result[i] = ConvertValue(values[i]);
        }

        return result.AsReadOnly();
    }

    private CssParserToken ConvertValue(CssComponentValue value)
    {
        switch (value)
        {
            case CssFunction function:
                return new CssParserFunction(function.Name, SplitArguments(function.Value));

            case CssSimpleBlock block:
                var name = block.AssociatedToken switch
                {
                    CssTokenType.LeftSquareBracket => "[]",
                    CssTokenType.LeftParenthesis => "()",
                    _ => "{}",
                };
                return new CssParserBlock(name, ConvertValues(block.Value));

            case CssPreservedToken preserved:
                var token = preserved.Token;
                switch (token.Type)
                {
                    case CssTokenType.Whitespace:
                        return " ";
                    case CssTokenType.Ident:
                        return new CssKeywordValue(token.Value);
                    case CssTokenType.Number:
                        return new CssUnitValue(token.NumericValue, "number");
                    case CssTokenType.Percentage:
                        return new CssUnitValue(token.NumericValue, "percent");
                    case CssTokenType.Dimension when IsCssUnit(token.Value):
                        return new CssUnitValue(token.NumericValue, token.Value);
                }

                // The other tokens (e.g. a string, a hash, a delim, or a dimension whose unit is not a CSS unit) are their text
                var start = Math.Clamp(token.Start, 0, _source.Length);
                var end = Math.Clamp(token.End, start, _source.Length);
                return _source[start..end];

            default:
                throw new InvalidOperationException("Unexpected component value");
        }
    }

    /// <summary>The arguments of a function: the component values between the top-level commas (none for an empty function).</summary>
    private List<IReadOnlyList<CssParserToken>> SplitArguments(List<CssComponentValue> values)
    {
        var args = new List<IReadOnlyList<CssParserToken>>();
        if (values.Count == 0)
            return args;

        var current = new List<CssParserToken>();
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                args.Add(current);
                current = [];
            }
            else
            {
                current.Add(ConvertValue(value));
            }
        }

        args.Add(current);
        return args;
    }

    /// <summary>Whether the unit of a dimension is a unit of CSS (not the "number" and "percent" pseudo-units of the Typed OM, e.g. 5number).</summary>
    private static bool IsCssUnit(string unit)
    {
        var lowercase = InfraAscii.ToAsciiLowercase(unit);
        return lowercase is not ("number" or "percent") && NumericType.FromUnit(lowercase) is not null;
    }

    private static bool IsWhitespace(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Whitespace };
}
