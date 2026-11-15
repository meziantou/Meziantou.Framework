using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Syntax;

/// <summary>A rule or a declaration produced by the parser.</summary>
internal abstract class CssSyntaxNode
{
}

/// <summary>An invalid rule or declaration (a parse error the parser recovered from); only reported for testing purposes.</summary>
internal sealed class CssSyntaxError(string kind) : CssSyntaxNode
{
    public string Kind { get; } = kind;
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#at-rule</summary>
internal sealed class CssAtRule(string name, List<CssComponentValue> prelude) : CssSyntaxNode
{
    public string Name { get; } = name;

    public List<CssComponentValue> Prelude { get; } = prelude;

    /// <summary>The block (its component values), or null for a statement at-rule.</summary>
    public CssSimpleBlock? Block { get; set; }
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#qualified-rule</summary>
internal sealed class CssQualifiedRule(List<CssComponentValue> prelude, CssSimpleBlock block) : CssSyntaxNode
{
    public List<CssComponentValue> Prelude { get; } = prelude;

    public CssSimpleBlock Block { get; set; } = block;
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#declaration</summary>
/// <remarks>
/// The value is a range of the component values the declaration was consumed from, which must not be modified: its list is
/// only created when it is used, as the value of a declaration is often found in <see cref="Values.ParsedValueCache"/>.
/// </remarks>
internal sealed class CssDeclaration(string name, List<CssComponentValue> values, int start, int count, bool important) : CssSyntaxNode
{
    private List<CssComponentValue>? _value;

    public string Name { get; } = name;

    public List<CssComponentValue> Value => _value ??= values.GetRange(start, count);

    /// <summary>The component values of <see cref="Value"/>, without creating its list.</summary>
    public ReadOnlySpan<CssComponentValue> ValueSpan => _value is null ? CollectionsMarshal.AsSpan(values).Slice(start, count) : CollectionsMarshal.AsSpan(_value);

    public bool Important { get; } = important;
}

/// <summary>
/// A stream of component values (https://drafts.csswg.org/css-syntax-3/#css-token-stream). The input is first parsed
/// into component values, so blocks and functions are single items.
/// </summary>
internal sealed class CssComponentValueStream(List<CssComponentValue> values)
{
    private int _index;

    /// <summary>The component values of the stream.</summary>
    public List<CssComponentValue> Values => values;

    public bool IsEmpty => _index >= values.Count;

    public CssComponentValue? Next => _index < values.Count ? values[_index] : null;

    public int Mark() => _index;

    public void Restore(int mark) => _index = mark;

    public CssComponentValue Consume() => values[_index++];

    public void Discard() => _index++;

    public bool NextIs(CssTokenType type) => Next is CssPreservedToken token && token.Type == type;

    public bool NextIsBlock(CssTokenType type) => Next is CssSimpleBlock block && block.AssociatedToken == type;

    public void DiscardWhitespace()
    {
        while (NextIs(CssTokenType.Whitespace))
        {
            _index++;
        }
    }
}

/// <summary>The parsing algorithms (https://drafts.csswg.org/css-syntax-3/#parser-algorithms).</summary>
internal static partial class CssParser
{
    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-a-stylesheet</summary>
    public static List<CssSyntaxNode> ParseStylesheet(string input, bool reportErrors = false) => ConsumeStylesheetContents(new CssComponentValueStream(ParseListOfComponentValues(input)), reportErrors);

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-stylesheet-contents</summary>
    /// <remarks>When <paramref name="topLevel"/> is false (the former "consume a list of rules" of nested contexts), CDO and CDC tokens are not ignored.</remarks>
    public static List<CssSyntaxNode> ConsumeStylesheetContents(CssComponentValueStream input, bool reportErrors, bool topLevel = true)
    {
        var rules = new List<CssSyntaxNode>();
        ConsumeStylesheetContents(input, rules, reportErrors, topLevel);
        return rules;
    }

    /// <summary>Consumes the contents of a style sheet into a list (e.g. a list that is reused).</summary>
    public static void ConsumeStylesheetContents(CssComponentValueStream input, List<CssSyntaxNode> rules, bool reportErrors, bool topLevel = true)
    {
        while (true)
        {
            switch (input.Next)
            {
                case null:
                    return;
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                case CssPreservedToken { Type: CssTokenType.Cdo or CssTokenType.Cdc } when topLevel:
                    input.Discard();
                    break;
                case CssPreservedToken { Type: CssTokenType.AtKeyword }:
                    if (ConsumeAtRule(input, nested: false) is { } atRule)
                    {
                        rules.Add(atRule);
                    }

                    break;
                default:
                    if (ConsumeQualifiedRule(input, stopToken: null, nested: false) is { } rule)
                    {
                        rules.Add(rule);
                    }
                    else if (reportErrors)
                    {
                        rules.Add(new CssSyntaxError("invalid"));
                    }

                    break;
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-at-rule</summary>
    public static CssAtRule? ConsumeAtRule(CssComponentValueStream input, bool nested)
    {
        var keyword = (CssPreservedToken)input.Consume();
        var rule = new CssAtRule(keyword.Value, ConsumePrelude(input, stopToken: CssTokenType.Semicolon, nested));
        switch (input.Next)
        {
            case CssPreservedToken { Type: CssTokenType.Semicolon }:
                input.Discard();
                break;
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block:
                input.Discard();
                rule.Block = block;
                break;
        }

        return rule;
    }

    /// <summary>
    /// Consumes the prelude of a rule, up to a {}-block, the stop token, or a } in a nested context. The values are counted
    /// first, so that the list is allocated once with its final size.
    /// </summary>
    private static List<CssComponentValue> ConsumePrelude(CssComponentValueStream input, CssTokenType? stopToken, bool nested)
    {
        var start = input.Mark();
        while (input.Next is { } next && !IsEndOfPrelude(next, stopToken, nested))
        {
            input.Discard();
        }

        var end = input.Mark();
        input.Restore(start);
        var prelude = new List<CssComponentValue>(end - start);
        while (input.Mark() < end)
        {
            prelude.Add(input.Consume());
        }

        return prelude;
    }

    private static bool IsEndOfPrelude(CssComponentValue value, CssTokenType? stopToken, bool nested) => value switch
    {
        CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } => true,
        CssPreservedToken token => token.Type == stopToken || (nested && token.Type == CssTokenType.RightCurlyBracket),
        _ => false,
    };

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-qualified-rule</summary>
    public static CssQualifiedRule? ConsumeQualifiedRule(CssComponentValueStream input, CssTokenType? stopToken, bool nested)
    {
        var prelude = ConsumePrelude(input, stopToken, nested);
        if (input.Next is not CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block)
            return null;

        input.Discard();

        // A prelude that looks like a custom property declaration is invalid
        if (LooksLikeCustomProperty(prelude))
            return null;

        return new CssQualifiedRule(prelude, block);
    }

    private static bool LooksLikeCustomProperty(List<CssComponentValue> prelude)
    {
        var index = 0;
        while (index < prelude.Count && prelude[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        if (index >= prelude.Count || prelude[index] is not CssPreservedToken { Type: CssTokenType.Ident } ident || !ident.Value.StartsWith("--", StringComparison.Ordinal))
            return false;

        index++;
        while (index < prelude.Count && prelude[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        return index < prelude.Count && prelude[index] is CssPreservedToken { Type: CssTokenType.Colon };
    }

    /// <summary>
    /// https://drafts.csswg.org/css-syntax-3/#consume-block-contents: the declarations and the rules of a block, in order.
    /// </summary>
    public static List<CssSyntaxNode> ConsumeBlockContents(CssComponentValueStream input, bool reportErrors = false)
    {
        var result = new List<CssSyntaxNode>();
        ConsumeBlockContents(input, result, reportErrors);
        return result;
    }

    /// <summary>Consumes the contents of a block into a list (e.g. a list that is reused).</summary>
    public static void ConsumeBlockContents(CssComponentValueStream input, List<CssSyntaxNode> result, bool reportErrors = false)
    {
        while (true)
        {
            switch (input.Next)
            {
                case null:
                    return;
                case CssPreservedToken { Type: CssTokenType.Whitespace or CssTokenType.Semicolon }:
                    input.Discard();
                    break;
                case CssPreservedToken { Type: CssTokenType.AtKeyword }:
                    if (ConsumeAtRule(input, nested: true) is { } atRule)
                    {
                        result.Add(atRule);
                    }

                    break;
                default:
                {
                    var mark = input.Mark();
                    if (ConsumeDeclaration(input, nested: true) is { } declaration)
                    {
                        result.Add(declaration);
                        break;
                    }

                    input.Restore(mark);
                    if (ConsumeQualifiedRule(input, CssTokenType.Semicolon, nested: true) is { } rule)
                    {
                        result.Add(rule);
                    }
                    else
                    {
                        if (reportErrors)
                        {
                            result.Add(new CssSyntaxError("invalid"));
                        }

                        // Consume the remnants of the invalid rule, up to the next semicolon
                        while (input.Next is not null && !input.NextIs(CssTokenType.Semicolon))
                        {
                            input.Discard();
                        }
                    }

                    break;
                }
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-declaration</summary>
    public static CssDeclaration? ConsumeDeclaration(CssComponentValueStream input, bool nested)
    {
        if (input.Next is not CssPreservedToken { Type: CssTokenType.Ident } name)
        {
            ConsumeRemnantsOfBadDeclaration(input, nested);
            return null;
        }

        input.Discard();
        input.DiscardWhitespace();
        if (!input.NextIs(CssTokenType.Colon))
        {
            ConsumeRemnantsOfBadDeclaration(input, nested);
            return null;
        }

        input.Discard();
        input.DiscardWhitespace();
        var start = input.Mark();
        while (input.Next is not null && !input.NextIs(CssTokenType.Semicolon) && !(nested && input.NextIs(CssTokenType.RightCurlyBracket)))
        {
            input.Discard();
        }

        var value = CollectionsMarshal.AsSpan(input.Values)[start..input.Mark()];

        // !important
        var important = false;
        var last = LastNonWhitespace(value, value.Length);
        if (last >= 0 && value[last] is CssPreservedToken { Type: CssTokenType.Ident } importantToken && Infra.InfraAscii.EqualsIgnoreAsciiCase(importantToken.Value, "important"))
        {
            var bang = LastNonWhitespace(value, last);
            if (bang >= 0 && value[bang] is CssPreservedToken bangToken && bangToken.IsDelim('!'))
            {
                value = value[..bang];
                important = true;
            }
        }

        value = value[..(LastNonWhitespace(value, value.Length) + 1)];

        // A {}-block is only allowed as the whole value of custom properties and of properties that accept it. Like the
        // browsers, the initial-value descriptor of @property, which accepts any <declaration-value>, is a custom property value.
        if (value.Length != 1 && !name.Value.StartsWith("--", StringComparison.Ordinal) && !Infra.InfraAscii.EqualsIgnoreAsciiCase(name.Value, "initial-value"))
        {
            foreach (var item in value)
            {
                if (item is CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket })
                    return null;
            }
        }

        return new CssDeclaration(name.Value, input.Values, start, value.Length, important);
    }

    private static int LastNonWhitespace(ReadOnlySpan<CssComponentValue> values, int end)
    {
        for (var i = end - 1; i >= 0; i--)
        {
            if (values[i] is not CssPreservedToken { Type: CssTokenType.Whitespace })
                return i;
        }

        return -1;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-the-remnants-of-a-bad-declaration</summary>
    private static void ConsumeRemnantsOfBadDeclaration(CssComponentValueStream input, bool nested)
    {
        while (input.Next is not null)
        {
            if (input.NextIs(CssTokenType.Semicolon))
            {
                input.Discard();
                return;
            }

            if (nested && input.NextIs(CssTokenType.RightCurlyBracket))
                return;

            input.Discard();
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-a-rule</summary>
    public static CssSyntaxNode ParseRule(string input)
    {
        var stream = new CssComponentValueStream(ParseListOfComponentValues(input));
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
            return new CssSyntaxError("empty");

        CssSyntaxNode? rule = stream.NextIs(CssTokenType.AtKeyword) ? ConsumeAtRule(stream, nested: false) : ConsumeQualifiedRule(stream, stopToken: null, nested: false);
        if (rule is null)
            return new CssSyntaxError("invalid");

        stream.DiscardWhitespace();
        return stream.IsEmpty ? rule : new CssSyntaxError("extra-input");
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-a-declaration</summary>
    public static CssSyntaxNode ParseDeclaration(string input)
    {
        var stream = new CssComponentValueStream(ParseListOfComponentValues(input));
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
            return new CssSyntaxError("empty");

        if (ConsumeDeclaration(stream, nested: false) is not { } declaration)
            return new CssSyntaxError("invalid");

        stream.DiscardWhitespace();
        return stream.IsEmpty ? declaration : new CssSyntaxError("extra-input");
    }

    /// <summary>
    /// Consumes the contents of a block (https://drafts.csswg.org/css-syntax-3/#consume-block-contents) from a string: the
    /// former "parse a block's contents" entry point, used by the tests.
    /// </summary>
    public static List<CssSyntaxNode> ParseBlockContents(string input, bool reportErrors = false) => ConsumeBlockContents(new CssComponentValueStream(ParseListOfComponentValues(input)), reportErrors);

    /// <summary>
    /// https://drafts.csswg.org/css-syntax-3/#parse-a-list-of-declarations: the declarations of a style attribute or of
    /// a CSS declaration block; the at-rules are discarded. The positions of the declarations are relative to <paramref name="preprocessedInput"/>.
    /// </summary>
    public static List<CssSyntaxNode> ParseDeclarationList(string input, out string preprocessedInput, bool reportErrors = false)
    {
        var stream = new CssComponentValueStream(ParseListOfComponentValues(input, out preprocessedInput));
        var declarations = new List<CssSyntaxNode>();
        while (stream.Next is not null)
        {
            if (stream.NextIs(CssTokenType.Whitespace) || stream.NextIs(CssTokenType.Semicolon))
            {
                stream.Discard();
            }
            else if (stream.NextIs(CssTokenType.AtKeyword))
            {
                ConsumeAtRule(stream, nested: false);
            }
            else if (ConsumeDeclaration(stream, nested: false) is { } declaration)
            {
                declarations.Add(declaration);
            }
            else if (reportErrors)
            {
                declarations.Add(new CssSyntaxError("invalid"));
            }
        }

        return declarations;
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-a-component-value</summary>
    public static CssComponentValue? ParseComponentValue(string input, out string? error)
    {
        error = null;
        var values = ParseListOfComponentValues(input);
        var start = 0;
        while (start < values.Count && values[start] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            start++;
        }

        var end = values.Count;
        while (end > start && values[end - 1] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            end--;
        }

        if (start == end)
        {
            error = "empty";
            return null;
        }

        if (end - start > 1)
        {
            error = "extra-input";
            return null;
        }

        return values[start];
    }
}
