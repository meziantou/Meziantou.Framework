using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The context of a substitution: the element whose value is computed (the root element of the calling context of a custom
/// function), the tree its tree-scoped names (the custom functions) are looked up from, and the custom function being
/// evaluated, whose local variables and parameters var() refers to.
/// </summary>
/// <param name="Style">The style of the element.</param>
/// <param name="TreeScope">The root of the tree of the style sheet of the value, or null for the document.</param>
/// <param name="Frame">The custom function being evaluated, or null for a value of the element.</param>
/// <param name="AttrElement">The element whose attributes attr() reads when it is not the element (the originating element of a ::part() rule), or null.</param>
internal readonly record struct SubstitutionContext(ComputedStyle Style, Node? TreeScope, CssFunctionFrame? Frame = null, Element? AttrElement = null, IReadOnlyDictionary<string, string>? NamespacePrefixes = null);

/// <summary>
/// Replaces the arbitrary substitution functions of a value (https://drafts.csswg.org/css-values-5/#arbitrary-substitution):
/// var() (https://drafts.csswg.org/css-variables/#using-variables), env() (https://drafts.csswg.org/css-env/), attr()
/// (https://drafts.csswg.org/css-values-5/#attr-notation), inherit() (https://drafts.csswg.org/css-values-5/#inherit-notation),
/// ident() (https://drafts.csswg.org/css-values-5/#ident), if() (https://drafts.csswg.org/css-values-5/#if-notation),
/// random-item() (https://drafts.csswg.org/css-values-5/#funcdef-random-item) and the &lt;dashed-function&gt;s (https://drafts.csswg.org/css-mixins-1/#substitute-a-dashed-function).
/// </summary>
internal static class CssSubstitution
{
    /// <summary>The maximum length of a value after substitution (https://drafts.csswg.org/css-variables/#long-variables).</summary>
    private const int MaxSubstitutedLength = 1024 * 1024;

    /// <summary>Returns whether the text may contain a substitution function (a fast check before parsing it).</summary>
    public static bool MayContainSubstitution(string text)
    {
        if (text.Contains("var(", StringComparison.OrdinalIgnoreCase) || text.Contains("env(", StringComparison.OrdinalIgnoreCase) || text.Contains("attr(", StringComparison.OrdinalIgnoreCase)
            || text.Contains("inherit(", StringComparison.OrdinalIgnoreCase) || text.Contains("ident(", StringComparison.OrdinalIgnoreCase) || text.Contains("if(", StringComparison.OrdinalIgnoreCase) || text.Contains("random-item(", StringComparison.OrdinalIgnoreCase))
            return true;

        // A <dashed-function> (--name(...))
        if (text.Contains("--", StringComparison.Ordinal) && text.Contains('(', StringComparison.Ordinal) && ValueMatcher.ContainsSubstitutionFunction(CssParser.ParseListOfComponentValues(text)))
            return true;

        // The name of a function can contain escapes (e.g. v\61 r() is var()): the text is tokenized
        return text.Contains('\\', StringComparison.Ordinal) && ValueMatcher.ContainsSubstitutionFunction(CssParser.ParseListOfComponentValues(text));
    }

    /// <summary>Substitutes the functions of a value of the element; returns null when the value is invalid at computed-value time.</summary>
    public static string? Substitute(string text, ComputedStyle style, Node? treeScope = null) => Substitute(text, new SubstitutionContext(style, treeScope));

    /// <summary>Substitutes the functions of a value; returns null when the value is invalid at computed-value time.</summary>
    public static string? Substitute(string text, SubstitutionContext context) => Substitute(text, context, out _);

    /// <summary>
    /// Substitutes the functions of a value; returns null when the value is invalid at computed-value time. The ranges of the
    /// result that are attr()-tainted (https://drafts.csswg.org/css-values-5/#attr-security) are returned, or null when there is none.
    /// </summary>
    public static string? Substitute(string text, SubstitutionContext context, out List<(int Start, int End)>? taintedRanges)
    {
        taintedRanges = null;
        var values = CssParser.ParseListOfComponentValues(text, out var source);
        if (!ValueMatcher.ContainsSubstitutionFunction(values))
            return text;

        var builder = new SubstitutionBuilder();
        if (!Append(builder, values, source, context))
            return null;

        // A value that only contains whitespace after substitution (e.g. the value of an empty custom property, which is a
        // space) is serialized as a space, like an empty value; an empty if() branch is empty. The trailing whitespace of a
        // substituted value is kept (if(style(--x): a ) is "a ")
        var result = builder.ToString();
        var trimmed = result.TrimStart();
        if (trimmed.Length == 0 && result.Length > 0)
            return " ";

        if (builder.TaintedRanges is { } ranges && trimmed.Length > 0)
        {
            var offset = result.Length - result.TrimStart().Length;
            taintedRanges = [.. ranges.Select(range => (range.Start - offset, range.End - offset))];
        }

        return trimmed;
    }

    /// <summary>
    /// Returns whether an attr()-tainted range of the value is used as or in a &lt;url&gt;: a url(), a src(), or a string
    /// that stands for a URL in image-set() or image() (https://drafts.csswg.org/css-values-5/#attr-security). The declaration
    /// is then invalid at computed-value time.
    /// </summary>
    public static bool UsesTaintedUrl(string value, List<(int Start, int End)>? taintedRanges)
    {
        if (taintedRanges is not { Count: > 0 })
            return false;

        return UsesTaintedUrl(CssParser.ParseListOfComponentValues(value), taintedRanges, imageFunction: false);

        static bool UsesTaintedUrl(List<CssComponentValue> values, List<(int Start, int End)> taintedRanges, bool imageFunction)
        {
            foreach (var value in values)
            {
                switch (value)
                {
                    case CssPreservedToken { Type: CssTokenType.Url }:
                    case CssPreservedToken { Type: CssTokenType.String } when imageFunction:
                    case CssFunction { Name: var name } when InfraAscii.ToAsciiLowercase(name) is "url" or "src":
                        if (taintedRanges.Exists(range => range.Start < value.End && value.Start < range.End))
                            return true;

                        break;

                    case CssFunction function:
                        if (UsesTaintedUrl(function.Value, taintedRanges, InfraAscii.ToAsciiLowercase(function.Name) is "image-set" or "-webkit-image-set" or "image"))
                            return true;

                        break;

                    case CssSimpleBlock block:
                        if (UsesTaintedUrl(block.Value, taintedRanges, imageFunction: false))
                            return true;

                        break;
                }
            }

            return false;
        }
    }

    private static bool Append(SubstitutionBuilder builder, List<CssComponentValue> values, string source, SubstitutionContext context)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when IsSubstitutionFunction(function.Name) || ValueMatcher.IsDashedFunction(function.Name):
                {
                    string? replacement;
                    bool tainted;
                    if (ValueMatcher.IsDashedFunction(function.Name))
                    {
                        replacement = ReplaceDashedFunction(function, source, context, out tainted);
                    }
                    else if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "ident"))
                    {
                        replacement = ReplaceIdent(function, source, context, out tainted);
                    }
                    else if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "if"))
                    {
                        replacement = ReplaceIf(function, source, context, out tainted);
                    }
                    else if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "random-item"))
                    {
                        replacement = ReplaceRandomItem(function, source, context, out tainted);
                    }
                    else if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "attr"))
                    {
                        replacement = ReplaceAttr(function, source, context, out tainted);
                    }
                    else
                    {
                        replacement = Replace(function, source, context, out tainted);
                    }

                    if (replacement is null)
                        return false;

                    builder.AppendValue(replacement, tainted);

                    // https://drafts.csswg.org/css-variables/#long-variables: a value that is too long (e.g. exponential
                    // substitutions) is invalid at computed-value time
                    if (builder.Length > MaxSubstitutedLength)
                        return false;

                    break;
                }

                case CssFunction function when ValueMatcher.ContainsSubstitutionFunction(function.Value):
                    builder.AppendRaw(CssSerializer.SerializeIdentifier(function.Name) + "(", TokenKind.Function, TokenKind.Other);
                    if (!Append(builder, function.Value, source, context))
                        return false;

                    builder.AppendRaw(")", TokenKind.Other, TokenKind.Other);
                    break;

                case CssSimpleBlock block when ValueMatcher.ContainsSubstitutionFunction(block.Value):
                {
                    var (open, close) = block.AssociatedToken switch
                    {
                        CssTokenType.LeftSquareBracket => ('[', ']'),
                        CssTokenType.LeftCurlyBracket => ('{', '}'),
                        _ => ('(', ')'),
                    };

                    builder.AppendRaw(open.ToString(), open == '(' ? TokenKind.LeftParenthesis : TokenKind.Other, TokenKind.Other);
                    if (!Append(builder, block.Value, source, context))
                        return false;

                    builder.AppendRaw(close.ToString(), TokenKind.Other, TokenKind.Other);
                    break;
                }

                case CssPreservedToken { Token: var token }:
                    builder.AppendRaw(GetSource(value, source), GetTokenKind(token), GetTokenKind(token));
                    break;

                case CssFunction:
                    builder.AppendRaw(GetSource(value, source), TokenKind.Function, TokenKind.Other);
                    break;

                default:
                    builder.AppendRaw(GetSource(value, source), value is CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } ? TokenKind.LeftParenthesis : TokenKind.Other, TokenKind.Other);
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// The kinds of tokens that must be separated by a comment when they follow each other
    /// (https://drafts.csswg.org/css-syntax-3/#serialization), e.g. two identifiers or a number followed by a percent sign.
    /// </summary>
    internal enum TokenKind
    {
        None,
        Other,
        Whitespace,
        Ident,
        Function,
        Url,
        BadUrl,
        AtKeyword,
        Hash,
        Number,
        Percentage,
        Dimension,
        Cdc,
        LeftParenthesis,
        NumberSign,
        HyphenMinus,
        CommercialAt,
        FullStop,
        PlusSign,
        Solidus,
        Asterisk,
        PercentSign,
    }

    internal static TokenKind GetTokenKind(CssToken token) => token.Type switch
    {
        CssTokenType.Whitespace => TokenKind.Whitespace,
        CssTokenType.Ident => TokenKind.Ident,
        CssTokenType.Function => TokenKind.Function,
        CssTokenType.Url => TokenKind.Url,
        CssTokenType.BadUrl => TokenKind.BadUrl,
        CssTokenType.AtKeyword => TokenKind.AtKeyword,
        CssTokenType.Hash => TokenKind.Hash,
        CssTokenType.Number => TokenKind.Number,
        CssTokenType.Percentage => TokenKind.Percentage,
        CssTokenType.Dimension => TokenKind.Dimension,
        CssTokenType.Cdc => TokenKind.Cdc,
        CssTokenType.LeftParenthesis => TokenKind.LeftParenthesis,
        CssTokenType.Delim => token.Value switch
        {
            "#" => TokenKind.NumberSign,
            "-" => TokenKind.HyphenMinus,
            "@" => TokenKind.CommercialAt,
            "." => TokenKind.FullStop,
            "+" => TokenKind.PlusSign,
            "/" => TokenKind.Solidus,
            "*" => TokenKind.Asterisk,
            "%" => TokenKind.PercentSign,
            _ => TokenKind.Other,
        },
        _ => TokenKind.Other,
    };

    /// <summary>Returns whether two consecutive tokens must be separated by a comment (https://drafts.csswg.org/css-syntax-3/#serialization).</summary>
    internal static bool RequiresComment(TokenKind first, TokenKind second)
    {
        var identOrNumber = second is TokenKind.Ident or TokenKind.Function or TokenKind.Url or TokenKind.BadUrl or TokenKind.Number or TokenKind.Percentage or TokenKind.Dimension;
        return first switch
        {
            TokenKind.Ident => identOrNumber || second is TokenKind.HyphenMinus or TokenKind.Cdc or TokenKind.LeftParenthesis,
            TokenKind.AtKeyword or TokenKind.Hash or TokenKind.Dimension => identOrNumber || second is TokenKind.HyphenMinus or TokenKind.Cdc,
            TokenKind.NumberSign or TokenKind.HyphenMinus => identOrNumber || second is TokenKind.HyphenMinus,
            TokenKind.Number => identOrNumber || second is TokenKind.PercentSign,
            TokenKind.CommercialAt => second is TokenKind.Ident or TokenKind.Function or TokenKind.Url or TokenKind.BadUrl or TokenKind.HyphenMinus or TokenKind.Cdc,
            TokenKind.FullStop or TokenKind.PlusSign => second is TokenKind.Number or TokenKind.Percentage or TokenKind.Dimension,
            TokenKind.Solidus => second is TokenKind.Asterisk,
            _ => false,
        };
    }

    /// <summary>
    /// The result of a substitution: the values are appended token by token, an empty comment separating the tokens that would
    /// otherwise be parsed differently (https://drafts.csswg.org/css-syntax-3/#serialization), and the substitution values that
    /// are attr()-tainted are tracked (https://drafts.csswg.org/css-values-5/#attr-security).
    /// </summary>
    private sealed class SubstitutionBuilder
    {
        private readonly StringBuilder _text = new();
        private TokenKind _last;

        public int Length => _text.Length;

        /// <summary>Whether an attr()-tainted value is part of the result.</summary>
        public bool Tainted => TaintedRanges is not null;

        /// <summary>The ranges of the result that are attr()-tainted.</summary>
        public List<(int Start, int End)>? TaintedRanges { get; private set; }

        /// <summary>Appends text whose first and last tokens are known.</summary>
        public void AppendRaw(string text, TokenKind first, TokenKind last)
        {
            if (text.Length == 0)
                return;

            if (RequiresComment(_last, first))
            {
                _text.Append("/**/");
            }

            _text.Append(text);
            _last = last;
        }

        /// <summary>
        /// Appends a substitution value: its tokens (the comments before the first token and after the last one are not part of
        /// it), as a whole attr()-tainted when it is.
        /// </summary>
        public void AppendValue(string value, bool tainted)
        {
            var tokenizer = new CssTokenizer(value);
            CssToken? first = null;
            CssToken last = default;
            while (tokenizer.ConsumeToken() is { Type: not CssTokenType.EndOfFile } token)
            {
                first ??= token;
                last = token;
            }

            if (first is not { } firstToken)
                return;

            var input = tokenizer.Input;
            var text = input[firstToken.Start..Math.Min(last.End, input.Length)];
            AppendRaw(text, GetTokenKind(firstToken), GetTokenKind(last));
            if (tainted)
            {
                (TaintedRanges ??= []).Add((_text.Length - text.Length, _text.Length));
            }
        }

        public override string ToString() => _text.ToString();
    }

    /// <summary>Appends text; an empty comment separates two tokens that would otherwise be parsed as one (e.g. a number followed by an identifier).</summary>
    internal static void AppendSeparated(StringBuilder builder, string text)
    {
        if (builder.Length > 0 && text.Length > 0 && IsNameOrNumberCharacter(builder[^1]) && (IsNameOrNumberCharacter(text[0]) || text[0] == '%'))
        {
            builder.Append("/**/");
        }

        builder.Append(text);
    }

    private static bool IsNameOrNumberCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' || c >= 0x80;

    private static string GetSource(CssComponentValue value, string source)
    {
        var start = Math.Clamp(value.Start, 0, source.Length);
        var end = Math.Clamp(value.End, start, source.Length);
        return source[start..end];
    }

    private static bool IsSubstitutionFunction(string name) => InfraAscii.ToAsciiLowercase(name) is "var" or "env" or "attr" or "inherit" or "ident" or "if" or "random-item";

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#replace-a-dashed-function: the arguments are substituted in the calling context,
    /// then the custom function is evaluated. The value is invalid when there is no such function.
    /// </summary>
    private static string? ReplaceDashedFunction(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        if (context.Style.Engine.FindFunction(function.Name, context.TreeScope, out var definitionScope) is not { } definition)
            return null;

        // An argument containing commas is written in a {} block (https://drafts.csswg.org/css-values-5/#component-function-commas).
        // The attr()-taint of the arguments is tracked by the function: its result is tainted when its value is.
        var arguments = new List<(string? Value, bool Tainted)>();
        if (ValueMatcher.RemoveWhitespace(function.Value).Count > 0)
        {
            foreach (var argument in ValueMatcher.SplitAtCommas(function.Value))
            {
                var values = ValueMatcher.TrimWhitespace(argument);
                if (values is [CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block])
                {
                    values = block.Value;
                }

                var builder = new SubstitutionBuilder();
                arguments.Add(Append(builder, values, source, context) ? (builder.ToString().Trim(), builder.Tainted) : (null, false));
            }
        }

        return CssFunctionFrame.Evaluate(definition, definitionScope, arguments, context, out tainted);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-values-5/#ident: ident( &lt;ident-arg&gt;+ ), where &lt;ident-arg&gt; is
    /// a &lt;string&gt;, an &lt;integer&gt; or an &lt;ident&gt;. The arguments are substituted, then the &lt;ident&gt; is the
    /// concatenation of their values (the integers are computed, e.g. a calc() that depends on the font size).
    /// </summary>
    private static string? ReplaceIdent(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        var builder = new SubstitutionBuilder();
        var success = Append(builder, function.Value, source, context);
        tainted = builder.Tainted;
        if (!success)
            return null;

        var arguments = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(builder.ToString()));
        if (arguments.Count == 0)
            return null;

        var value = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (argument is CssPreservedToken { Type: CssTokenType.String or CssTokenType.Ident } token)
            {
                value.Append(token.Token.Value);
            }
            // The font-relative lengths of a font property refer to the font of the parent (https://drafts.csswg.org/css-values-4/#font-relative-lengths)
            else if (CustomPropertySyntax.ComputeGrammar(context.Style, "<integer>", CssSerializer.SerializeValue([argument], property: null), baseUrl: null, fontSizeContext: s_computedProperty.Property?.StartsWith("font-", StringComparison.Ordinal) == true) is { } integer)
            {
                value.Append(integer);
            }
            else
            {
                return null;
            }
        }

        return CssSerializer.SerializeIdentifier(value.ToString());
    }

    /// <summary>
    /// https://drafts.csswg.org/css-values-5/#if-notation: if( [ &lt;if-branch&gt; ; ]* &lt;if-branch&gt; ;? ), where
    /// &lt;if-branch&gt; = &lt;if-condition&gt; : &lt;declaration-value&gt;?. The value of the first branch whose condition is
    /// true is substituted (only that value: a cycle in another branch does not matter); when no condition is true, the value
    /// is invalid at computed-value time. A branch without a colon, or a top-level ! makes the whole if() invalid. The result
    /// is attr()-tainted when the value of the branch is, or when an evaluated condition uses an attr()-tainted value
    /// (https://drafts.csswg.org/css-values-5/#attr-security).
    /// </summary>
    private static string? ReplaceIf(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        var branches = new List<List<CssComponentValue>> { new() };
        foreach (var value in function.Value)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Semicolon })
            {
                branches.Add([]);
            }
            else if (value is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.IsDelim('!'))
            {
                return null;
            }
            else
            {
                branches[^1].Add(value);
            }
        }

        // The last ; is optional
        if (branches.Count > 1 && ValueMatcher.RemoveWhitespace(branches[^1]).Count == 0)
        {
            branches.RemoveAt(branches.Count - 1);
        }

        var parsed = new List<(List<CssComponentValue> Condition, List<CssComponentValue> Value)>(branches.Count);
        foreach (var branch in branches)
        {
            var colon = branch.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Colon });
            if (colon < 0 || ValueMatcher.RemoveWhitespace(branch[..colon]).Count == 0)
                return null;

            parsed.Add((branch[..colon], branch[(colon + 1)..]));
        }

        // A condition whose style query depends on the scrollbar gutters that the viewport units subtract while they are being
        // determined is a cycle: the if() is invalid at computed-value time (https://github.com/w3c/csswg-drafts/issues/13189)
        var engine = context.Style.Engine;
        var cycles = engine.ViewportUnitCycleCount;
        foreach (var (condition, value) in parsed)
        {
            var matches = EvaluateIfCondition(condition, source, context, ref tainted);
            if (engine.ViewportUnitCycleCount != cycles)
                return null;

            if (matches != true)
                continue;

            var builder = new SubstitutionBuilder();
            if (!Append(builder, value, source, context))
                return null;

            tainted |= builder.Tainted;

            // The whitespace after the colon is not part of the value, unlike the whitespace before the closing parenthesis
            return builder.ToString().TrimStart();
        }

        return null;
    }

    /// <summary>
    /// Evaluates an &lt;if-condition&gt; = &lt;boolean-expr[ &lt;if-test&gt; ]&gt; | else. The arbitrary substitution functions
    /// of the condition are substituted first (a var() can be else), except in the style() tests that substitute their values
    /// themselves. A condition that cannot be parsed is false. <paramref name="tainted"/> is set when the condition uses an
    /// attr()-tainted value.
    /// </summary>
    private static bool? EvaluateIfCondition(List<CssComponentValue> condition, string source, SubstitutionContext context, ref bool tainted)
    {
        if (condition.Exists(v => v is CssFunction function && (IsSubstitutionFunction(function.Name) || ValueMatcher.IsDashedFunction(function.Name))))
        {
            var builder = new SubstitutionBuilder();
            var success = Append(builder, condition, source, context);
            tainted |= builder.Tainted;
            if (!success)
                return false;

            condition = CssParser.ParseListOfComponentValues(builder.ToString(), out source);
        }

        var tokens = ValueMatcher.RemoveWhitespace(condition);
        if (tokens is [CssPreservedToken { Type: CssTokenType.Ident } elseKeyword] && elseKeyword.Token.IsIdent("else"))
            return true;

        return EvaluateIfBooleanExpression(tokens, source, context, depth: 0, ref tainted);
    }

    /// <summary>&lt;boolean-expr[ &lt;if-test&gt; ]&gt; (https://drafts.csswg.org/css-values-5/#typedef-boolean-expr), with the Kleene logic of the unknown results (null).</summary>
    private static bool? EvaluateIfBooleanExpression(List<CssComponentValue> tokens, string source, SubstitutionContext context, int depth, ref bool tainted)
    {
        if (tokens.Count == 0 || depth > 32)
            return false;

        // The operands are functions or () blocks (a <general-enclosed> is unknown); another token makes the condition invalid
        for (var i = tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } first && first.Token.IsIdent("not") ? 1 : 0; i < tokens.Count; i += 2)
        {
            if (tokens[i] is not (CssFunction or CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis }))
                return false;
        }

        if (tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } not && not.Token.IsIdent("not"))
            return tokens.Count == 2 ? !EvaluateIfTestInParens(tokens[1], source, context, depth, ref tainted) : false;

        bool? result = EvaluateIfTestInParens(tokens[0], source, context, depth, ref tainted);
        string? combinator = null;
        for (var i = 1; i < tokens.Count; i += 2)
        {
            if (i + 1 >= tokens.Count || tokens[i] is not CssPreservedToken { Type: CssTokenType.Ident } word)
                return false;

            // "and" and "or" cannot be mixed at the same level
            var keyword = InfraAscii.ToAsciiLowercase(word.Token.Value);
            if (keyword is not ("and" or "or") || (combinator is not null && combinator != keyword))
                return false;

            combinator = keyword;
            var next = EvaluateIfTestInParens(tokens[i + 1], source, context, depth, ref tainted);
            result = keyword == "and" ? (result == false || next == false ? false : result is null || next is null ? null : true) : (result == true || next == true ? true : result is null || next is null ? null : false);
        }

        return result;
    }

    /// <summary>
    /// ( &lt;boolean-expr&gt; ) | &lt;if-test&gt; | &lt;general-enclosed&gt; (unknown), where &lt;if-test&gt; =
    /// supports( [ &lt;ident&gt; : &lt;declaration-value&gt; ] | &lt;supports-condition&gt; ) | media( &lt;media-feature&gt; | &lt;media-condition&gt; ) | style( &lt;style-query&gt; ).
    /// </summary>
    private static bool? EvaluateIfTestInParens(CssComponentValue value, string source, SubstitutionContext context, int depth, ref bool tainted)
    {
        switch (value)
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
                return EvaluateIfBooleanExpression(ValueMatcher.RemoveWhitespace(block.Value), source, context, depth + 1, ref tainted);

            case CssFunction function:
                switch (InfraAscii.ToAsciiLowercase(function.Name))
                {
                    case "style":
                    {
                        if (Om.ContainerQueryParser.ParseStyleTest(function.Value, source) is not { } query)
                            return false;

                        var queryContext = new ContainerQueryContext(context.Style) { IsIfTest = true, Frame = context.Frame };
                        var result = query.Evaluate(queryContext);
                        tainted |= queryContext.UsesTaintedValue;
                        return result;
                    }

                    case "media":
                    {
                        // A <media-feature> is written without its parentheses
                        var tokens = ValueMatcher.RemoveWhitespace(function.Value);
                        if (tokens.Count == 0 || tokens.Exists(v => v is CssPreservedToken { Type: CssTokenType.Comma }))
                            return false;

                        var text = GetSource(function.Value, source);
                        if (tokens[0] is not CssSimpleBlock && !(tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } mediaNot && mediaNot.Token.IsIdent("not")))
                        {
                            text = "(" + text + ")";
                        }

                        return Om.MediaQueryEvaluator.Matches(text, context.Style.Environment);
                    }

                    // The declarations are parsed in the mode of the document (the quirky lengths are valid in quirks mode)
                    case "supports":
                        return ValueMatcher.RemoveWhitespace(function.Value).Count > 0 && Om.Css.Supports(GetSource(function.Value, source), context.Style.Element.NodeDocument.Mode == DocumentMode.Quirks);

                    default:
                        return null;
                }

            default:
                return null;
        }
    }

    private static string GetSource(List<CssComponentValue> values, string source)
    {
        if (values.Count == 0)
            return "";

        var start = Math.Clamp(values[0].Start, 0, source.Length);
        var end = Math.Clamp(values[^1].End, start, source.Length);
        return source[start..end];
    }

    /// <summary>
    /// https://drafts.csswg.org/css-values-5/#funcdef-random-item: random-item( &lt;random-key&gt; , [ &lt;declaration-value&gt;? ]# ).
    /// The item at the index chosen by the random base value of the key (like random(), auto is element-scoped) is substituted;
    /// an item that contains commas is written in a {} block. An empty item, or an invalid key, is invalid at computed-value time.
    /// The result is attr()-tainted when the item or the key is.
    /// </summary>
    private static string? ReplaceRandomItem(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        var arguments = ValueMatcher.SplitAtCommas(function.Value);
        if (arguments.Count < 2)
            return null;

        // The key can be provided by a substitution function (e.g. var(--key))
        var key = arguments[0];
        var keySource = source;
        if (ValueMatcher.ContainsSubstitutionFunction(key))
        {
            var builder = new SubstitutionBuilder();
            if (!Append(builder, key, source, context))
                return null;

            tainted = builder.Tainted;
            key = CssParser.ParseListOfComponentValues(builder.ToString(), out keySource);
        }

        var significantKey = ValueMatcher.RemoveWhitespace(key);
        double baseValue;
        if (significantKey is [CssPreservedToken fixedKeyword, var fixedValue] && fixedKeyword.Token.IsIdent("fixed"))
        {
            var number = fixedValue switch
            {
                CssPreservedToken { Type: CssTokenType.Number } token => token.Token.NumericValue,
                CssFunction math when CssCalc.Evaluate(math, CssCalcContext.Default) is { IsResolved: true, Unit: "" } result => result.Value,
                _ => double.NaN,
            };

            if (double.IsNaN(number))
                return null;

            baseValue = Math.Clamp(number, 0, CssRandom.MaxBaseValue);
        }
        else if (CssRandom.ParseSharing(significantKey) is { } sharing)
        {
            if (sharing.ScopeKeyword is "auto")
            {
                sharing = sharing with { ElementScoped = true, ScopeKeyword = null, UaIdent = "ua-random-item-" + GetSource(key, keySource).Trim() + "-" + (arguments.Count - 1) };
            }

            baseValue = context.Style.Engine.GetRandomBaseValue(sharing, context.Style.Element, context.Style.PseudoElement);
        }
        else
        {
            return null;
        }

        var items = arguments.Count - 1;
        var item = ValueMatcher.TrimWhitespace(arguments[1 + Math.Min((int)Math.Floor(baseValue * items), items - 1)]);
        if (item is [CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block])
        {
            item = block.Value;
        }

        if (ValueMatcher.RemoveWhitespace(item).Count == 0)
            return null;

        var value = new SubstitutionBuilder();
        if (!Append(value, item, source, context))
            return null;

        tainted |= value.Tainted;
        return value.ToString().Trim();
    }

    /// <summary>Returns the value of a substitution function, or null when it is invalid.</summary>
    private static string? Replace(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        // name [, fallback]
        tainted = false;
        var arguments = function.Value;
        var index = SkipWhitespace(arguments, 0);
        if (index >= arguments.Count)
            return null;

        var comma = arguments.FindIndex(index, v => v is CssPreservedToken { Type: CssTokenType.Comma });
        var fallback = comma < 0 ? null : arguments[(comma + 1)..];
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        // The substitution value of a var() whose name or value is attr()-tainted is attr()-tainted
        // (https://drafts.csswg.org/css-values-5/#attr-security)
        string? result;
        switch (name)
        {
            case "var":
                result = ReplaceVar(arguments, index, comma, source, context, out tainted);
                break;
            case "env":
                result = ReplaceEnv(arguments, index, comma, context.Style);
                break;
            default:
                result = ReplaceInherit(arguments, index, comma, context, out tainted);
                break;
        }

        if (result is not null)
            return result;

        if (fallback is null)
            return null;

        // The fallback can contain substitution functions itself; it is tainted by a tainted name of var()
        var builder = new SubstitutionBuilder();
        if (!Append(builder, fallback, source, context))
            return null;

        tainted |= builder.Tainted;
        return builder.ToString().Trim();
    }

    private static int SkipWhitespace(List<CssComponentValue> values, int index)
    {
        while (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-variables-2/#funcdef-var: the name argument is a custom property name, possibly in a {}
    /// block or provided by substitution functions (var(var(--name))); another value is invalid (the fallback is used). In a
    /// custom function, the local variables and the parameters shadow the custom properties
    /// (https://drafts.csswg.org/css-mixins-1/#locally-substitute-a-var).
    /// </summary>
    private static string? ReplaceVar(List<CssComponentValue> arguments, int index, int comma, string source, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        var nameValues = ValueMatcher.RemoveWhitespace(arguments[index..(comma < 0 ? arguments.Count : comma)]);
        if (ValueMatcher.ContainsSubstitutionFunction(nameValues))
        {
            var builder = new SubstitutionBuilder();
            if (!Append(builder, nameValues, source, context))
                return null;

            tainted = builder.Tainted;
            nameValues = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(builder.ToString()));
        }

        if (nameValues is [CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block])
        {
            nameValues = ValueMatcher.RemoveWhitespace(block.Value);
        }

        if (nameValues is not [CssPreservedToken { Type: CssTokenType.Ident } nameToken] || !CssPropertyRegistry.IsCustomProperty(nameToken.Token.Value))
            return null;

        if (context.Frame is { } frame)
        {
            var variable = frame.GetVariable(nameToken.Token.Value, out var variableTainted);
            tainted |= variableTainted;
            return variable;
        }

        var value = context.Style.GetCustomPropertyValue(nameToken.Token.Value);
        tainted |= context.Style.IsCustomPropertyTainted(nameToken.Token.Value);
        return value;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-values-5/#inherit-notation: the value of the custom property on the parent element; in a
    /// custom function, the value in its calling context (https://drafts.csswg.org/css-mixins-1/#resolve-function-styles).
    /// </summary>
    private static string? ReplaceInherit(List<CssComponentValue> arguments, int index, int comma, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        if (arguments[index] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken || !CssPropertyRegistry.IsCustomProperty(nameToken.Token.Value))
            return null;

        if (SkipWhitespace(arguments, index + 1) != (comma < 0 ? arguments.Count : comma))
            return null;

        var name = nameToken.Token.Value;
        if (context.Frame is { } frame)
            return frame.GetCallerVariable(name, out tainted);

        if (context.Style.Parent is not { } parent)
            return null;

        var value = parent.GetCustomPropertyValue(name);
        tainted = parent.IsCustomPropertyTainted(name);
        return value;
    }

    /// <summary>https://drafts.csswg.org/css-env/#env-function: there is no user agent chrome, so the insets are zero.</summary>
    private static string? ReplaceEnv(List<CssComponentValue> arguments, int index, int comma, ComputedStyle style)
    {
        if (arguments[index] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken)
            return null;

        // The indices of the two-dimensional variables (viewport-segment-*) are not supported
        var end = SkipWhitespace(arguments, index + 1);
        if (end != (comma < 0 ? arguments.Count : comma))
            return null;

        return GetEnvironmentVariable(nameToken.Token.Value, style.Environment);
    }

    /// <summary>
    /// Returns the value of a UA-defined environment variable (https://drafts.csswg.org/css-env-1/#environment), or null when
    /// it is unknown: there is no display cutout, virtual keyboard nor title bar.
    /// </summary>
    internal static string? GetEnvironmentVariable(string name, Om.MediaEnvironment environment) => name switch
    {
        "safe-area-inset-top" or "safe-area-inset-right" or "safe-area-inset-bottom" or "safe-area-inset-left" => "0px",
        "safe-area-max-inset-top" or "safe-area-max-inset-right" or "safe-area-max-inset-bottom" or "safe-area-max-inset-left" => "0px",
        "keyboard-inset-top" or "keyboard-inset-right" or "keyboard-inset-bottom" or "keyboard-inset-left" or "keyboard-inset-width" or "keyboard-inset-height" => "0px",
        "titlebar-area-x" or "titlebar-area-y" or "titlebar-area-height" => "0px",
        "titlebar-area-width" => CssSerializer.SerializeNumber(environment.Width) + "px",
        "preferred-text-scale" => "1",
        _ => null,
    };


    /// <summary>The kind of the &lt;attr-type&gt; of an attr() function.</summary>
    internal enum AttrTypeKind
    {
        /// <summary>No &lt;attr-type&gt;: the value is a string.</summary>
        None,

        /// <summary>raw-string: the value is a string.</summary>
        RawString,

        /// <summary>number: the value is a &lt;number-token&gt;.</summary>
        Number,

        /// <summary>An &lt;attr-unit&gt;: the value is a number followed by the unit.</summary>
        Unit,

        /// <summary>type( &lt;syntax&gt; ): the value is parsed according to the syntax.</summary>
        Type,
    }

    /// <summary>The first argument of an attr() function, parsed as &lt;attr-name&gt; &lt;attr-type&gt;?.</summary>
    /// <param name="Name">The local name of the attribute.</param>
    /// <param name="Unit">The unit of an &lt;attr-unit&gt; (lowercase, or % for a percentage), or null when it is not a known unit.</param>
    /// <param name="Syntax">The syntax of type().</param>
    /// <param name="Prefix">The namespace prefix of the name (ns|name), or null when the attribute is in the null namespace (name or |name).</param>
    internal readonly record struct AttrArguments(string Name, AttrTypeKind Kind, string? Unit, CustomPropertySyntax? Syntax, string? Prefix = null);

    /// <summary>
    /// The attributes whose value is being substituted by attr() (https://drafts.csswg.org/css-values-5/#substitute-arbitrary-substitution-function):
    /// the «"attribute", name» substitution contexts that are guarded. Reading an attribute that is guarded marks the contexts
    /// of the cycle as cyclic. The guards of a property are isolated from the ones of the properties it references
    /// (<see cref="BeginPropertyComputation"/>), so its value does not depend on the order in which the properties are computed.
    /// </summary>
    [ThreadStatic]
    private static List<AttributeGuard>? s_attributeGuards;

    internal sealed class AttributeGuard(Element element, string name)
    {
        public Element Element { get; } = element;

        public string Name { get; } = name;

        public bool Cyclic { get; set; }
    }

    /// <summary>The property whose value is being computed, and the number of custom functions it invoked (see <see cref="NextInvocation"/>).</summary>
    [ThreadStatic]
    private static (string Property, int Invocations) s_computedProperty;

    /// <summary>
    /// Starts the computation of the value of a property: the attributes being substituted by the property that references it
    /// are not guarded during its computation, and the invocations of the custom functions are counted from the start. Dispose
    /// the result to restore the state of the computation of the outer property.
    /// </summary>
    internal static PropertyComputationScope BeginPropertyComputation(string property)
    {
        var scope = new PropertyComputationScope(s_attributeGuards is { Count: > 0 } guards ? guards : null, s_computedProperty);
        if (scope.SavedGuards is not null)
        {
            s_attributeGuards = null;
        }

        s_computedProperty = (property, 0);
        return scope;
    }

    internal readonly struct PropertyComputationScope(List<AttributeGuard>? savedGuards, (string Property, int Invocations) savedProperty) : IDisposable
    {
        public List<AttributeGuard>? SavedGuards { get; } = savedGuards;

        public void Dispose()
        {
            if (SavedGuards is not null)
            {
                s_attributeGuards = SavedGuards;
            }

            s_computedProperty = savedProperty;
        }
    }

    /// <summary>
    /// Returns the identifier of a new invocation of a custom function by the property being computed: the name of the property
    /// and the number of the invocation, so that it is the same each time the value of the element is computed.
    /// </summary>
    internal static string NextInvocation()
    {
        var (property, invocations) = s_computedProperty;
        invocations++;
        s_computedProperty = (property, invocations);
        return (property ?? "") + "-" + invocations.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns whether the attribute is being substituted (a cycle): the substitution contexts from the one of the attribute
    /// are then marked as cyclic.
    /// </summary>
    private static bool DetectAttributeCycle(Element element, string name)
    {
        if (s_attributeGuards is not { } guards)
            return false;

        var index = guards.FindIndex(guard => guard.Element == element && guard.Name == name);
        if (index < 0)
            return false;

        for (var i = index; i < guards.Count; i++)
        {
            guards[i].Cyclic = true;
        }

        return true;
    }

    /// <summary>
    /// Parses the first argument of an attr() function (after substitution): &lt;attr-name&gt; &lt;attr-type&gt;?, where
    /// &lt;attr-name&gt; = [ &lt;ident-token&gt;? '|' ]? &lt;ident-token&gt; (the prefix is resolved by the @namespace rules of the style sheet) and
    /// &lt;attr-type&gt; = type( &lt;syntax&gt; ) | raw-string | number | &lt;attr-unit&gt;. Returns null on failure.
    /// </summary>
    internal static AttrArguments? ParseAttrArguments(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.TrimWhitespace(values);
        var index = 0;

        // The components of the name are not separated by whitespace
        string? prefix = null;
        if (tokens is [CssPreservedToken bar, ..] && bar.Token.IsDelim('|'))
        {
            index++;
        }
        else if (tokens is [CssPreservedToken { Type: CssTokenType.Ident } prefixToken, CssPreservedToken prefixBar, CssPreservedToken { Type: CssTokenType.Ident }, ..] && prefixBar.Token.IsDelim('|'))
        {
            prefix = prefixToken.Token.Value;
            index += 2;
        }

        if (index >= tokens.Count || tokens[index] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken)
            return null;

        index++;
        if (index < tokens.Count && tokens[index] is CssPreservedToken namespaceBar && namespaceBar.Token.IsDelim('|'))
            return null;

        while (index < tokens.Count && tokens[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        if (index == tokens.Count)
            return new AttrArguments(nameToken.Token.Value, AttrTypeKind.None, Unit: null, Syntax: null, prefix);

        if (index != tokens.Count - 1)
            return null;

        switch (tokens[index])
        {
            case CssPreservedToken { Type: CssTokenType.Ident } keyword when keyword.Token.IsIdent("raw-string"):
                return new AttrArguments(nameToken.Token.Value, AttrTypeKind.RawString, Unit: null, Syntax: null, prefix);

            case CssPreservedToken { Type: CssTokenType.Ident } keyword when keyword.Token.IsIdent("number"):
                return new AttrArguments(nameToken.Token.Value, AttrTypeKind.Number, Unit: null, Syntax: null, prefix);

            case CssPreservedToken { Type: CssTokenType.Ident } unit:
            {
                // An unknown unit is valid: it triggers the fallback
                var lower = InfraAscii.ToAsciiLowercase(unit.Token.Value);
                return new AttrArguments(nameToken.Token.Value, AttrTypeKind.Unit, ValueMatcher.IsKnownUnit(lower) ? lower : null, Syntax: null, prefix);
            }

            case CssPreservedToken percent when percent.Token.IsDelim('%'):
                return new AttrArguments(nameToken.Token.Value, AttrTypeKind.Unit, "%", Syntax: null, prefix);

            case CssFunction { Name: var functionName } typeFunction when InfraAscii.EqualsIgnoreAsciiCase(functionName, "type"):
            {
                // <url> is not allowed as a <syntax-single-component>
                if (CustomPropertySyntax.ParseSyntax(typeFunction.Value) is not { } definition || CustomPropertySyntax.Parse(definition.Text) is not { } syntax)
                    return null;

                foreach (var component in syntax.Components)
                {
                    if (component.IsDataType && component.Name == "url")
                        return null;
                }

                return new AttrArguments(nameToken.Token.Value, AttrTypeKind.Type, Unit: null, syntax, prefix);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-values-5/#replace-an-attr-function: attr( &lt;attr-name&gt; &lt;attr-type&gt;? , &lt;declaration-value&gt;? ).
    /// The first argument can be provided by substitution functions. Without type (or with raw-string), the value is a string;
    /// with number or a unit, it is a number; with type(), the substitution functions of the value are substituted (the
    /// attribute is guarded: an attribute that references itself is a cycle) and the result must match the syntax. When the
    /// attribute is missing or its value is invalid, the fallback is substituted (an empty string by default when there is no
    /// type). The value of the attribute is attr()-tainted (https://drafts.csswg.org/css-values-5/#attr-security).
    /// </summary>
    private static string? ReplaceAttr(CssFunction function, string source, SubstitutionContext context, out bool tainted)
    {
        tainted = false;
        var comma = function.Value.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
        var first = comma < 0 ? function.Value : function.Value[..comma];
        var fallback = comma < 0 ? null : function.Value[(comma + 1)..];

        AttrArguments? arguments;
        if (ValueMatcher.ContainsSubstitutionFunction(first))
        {
            var builder = new SubstitutionBuilder();
            var success = Append(builder, first, source, context);
            tainted = builder.Tainted;
            arguments = success ? ParseAttrArguments(CssParser.ParseListOfComponentValues(builder.ToString())) : null;
        }
        else
        {
            // The arguments are not valid at parse time: the attr() function is invalid, whatever its fallback
            arguments = ParseAttrArguments(first);
            if (arguments is null)
                return null;
        }

        if (arguments is { } attr && GetAttrValue(attr, context) is { } value)
        {
            tainted = true;
            return value;
        }

        if (fallback is null)
            return arguments is { Kind: AttrTypeKind.None } ? "\"\"" : null;

        var fallbackBuilder = new SubstitutionBuilder();
        if (!Append(fallbackBuilder, fallback, source, context))
            return null;

        tainted |= fallbackBuilder.Tainted;
        return fallbackBuilder.ToString().Trim();
    }

    /// <summary>Returns the value of the attribute of an attr() function, or null when it is missing or invalid (the fallback is used).</summary>
    private static string? GetAttrValue(AttrArguments arguments, SubstitutionContext context)
    {
        var element = context.AttrElement ?? context.Style.Element;
        var name = GetAttrName(element, arguments);
        string? attributeValue;
        if (arguments.Prefix is { } prefix)
        {
            // An undeclared prefix matches no attribute
            if (context.NamespacePrefixes?.GetValueOrDefault(prefix) is not { } namespaceUri)
                return null;

            attributeValue = element.GetAttributeValue(namespaceUri, arguments.Name);
            name = "{" + namespaceUri + "}" + arguments.Name;
        }
        else
        {
            attributeValue = element.GetAttributeValue(name);
        }

        if (attributeValue is null)
            return null;

        switch (arguments.Kind)
        {
            case AttrTypeKind.None or AttrTypeKind.RawString:
                return DetectAttributeCycle(element, name) ? null : CssSerializer.SerializeString(attributeValue);

            case AttrTypeKind.Number or AttrTypeKind.Unit:
            {
                if (DetectAttributeCycle(element, name) || ParseAttrNumber(attributeValue) is not { } number)
                    return null;

                if (arguments.Kind == AttrTypeKind.Number)
                    return CssSerializer.SerializeNumber(number);

                return arguments.Unit is { } unit ? CssSerializer.SerializeNumber(number) + (unit == "%" ? "%" : CssSerializer.SerializeIdentifier(unit)) : null;
            }

            default:
            {
                // The substitution functions of the value are substituted in the «"attribute", name» substitution context (and in
                // the context of the attr() function: the local variables of a custom function)
                if (DetectAttributeCycle(element, name))
                    return null;

                var guard = new AttributeGuard(element, name);
                var guards = s_attributeGuards ??= [];
                guards.Add(guard);
                string? substituted;
                try
                {
                    substituted = MayContainSubstitution(attributeValue) ? Substitute(attributeValue, context) : attributeValue;
                }
                finally
                {
                    guards.Remove(guard);
                }

                if (substituted is null || guard.Cyclic)
                    return null;

                // https://drafts.csswg.org/css-values-5/#parse-with-a-syntax: the value of the first matching component of the
                // syntax; the values of the data types are serialized as specified values (e.g. the units are lowercase)
                var values = CssParser.ParseListOfComponentValues(substituted);
                if (!arguments.Syntax!.TryMatch(values, out var component))
                    return null;

                if (component is { IsDataType: true } && component.Name is not ("string" or "custom-ident"))
                    return CssSerializer.SerializeValue(ValueMatcher.RemoveWhitespace(values), property: null);

                return substituted.Trim();
            }
        }
    }

    /// <summary>Returns the name of the attribute of an attr() function: the names of the HTML elements of an HTML document are ASCII case-insensitive.</summary>
    internal static string GetAttrName(Element element, AttrArguments arguments) => element.IsHtmlNamespace && element.NodeDocument.IsHtmlDocument ? InfraAscii.ToAsciiLowercase(arguments.Name) : arguments.Name;

    /// <summary>Parses the value of an attribute as a &lt;number-token&gt; (after stripping the leading and trailing whitespace).</summary>
    internal static double? ParseAttrNumber(string value)
    {
        var tokenizer = new CssTokenizer(value.Trim([' ', '\t', '\n', '\r', '\f']));
        var token = tokenizer.ConsumeToken();
        if (token.Type != CssTokenType.Number || tokenizer.ConsumeToken().Type != CssTokenType.EndOfFile)
            return null;

        return token.NumericValue;
    }
}
