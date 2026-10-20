using Meziantou.Framework.WebPlatform.Css.ParserApi;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

// The CSS Parser API (https://wicg.github.io/css-parser-api/#parsing-api), a draft that no browser implements: the
// operations run the entry points of CSS Syntax (https://drafts.csswg.org/css-syntax-3/#parser-entry-points). In the
// scripts, the operations that take a CSSStringSource also accept a ReadableStream of bytes (see WebIdlRealm.CssParserApi.cs).
public static partial class Css
{
    /// <summary>
    /// Parses a style sheet (https://wicg.github.io/css-parser-api/#dom-css-parsestylesheet,
    /// https://drafts.csswg.org/css-syntax-3/#parse-stylesheet): its rules. The task is completed when the method returns.
    /// </summary>
    /// <exception cref="TypeErrorException">A value of <see cref="CssParserOptions.AtRules"/> is not a block production.</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsestylesheet"/>
    [IdlMember("parseStylesheet", "https://wicg.github.io/css-parser-api/#dom-css-parsestylesheet")]
    public static Task<IReadOnlyList<CssParserRule>> ParseStylesheet(string css, CssParserOptions? options = null) => Task.FromResult(CssParserApiParser.ParseStylesheet(css, options));

    /// <summary>
    /// Parses a list of rules (https://wicg.github.io/css-parser-api/#dom-css-parserulelist,
    /// https://drafts.csswg.org/css-syntax-3/#parse-stylesheet-contents). The task is completed when the method returns.
    /// </summary>
    /// <exception cref="TypeErrorException">A value of <see cref="CssParserOptions.AtRules"/> is not a block production.</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parserulelist"/>
    [IdlMember("parseRuleList", "https://wicg.github.io/css-parser-api/#dom-css-parserulelist")]
    public static Task<IReadOnlyList<CssParserRule>> ParseRuleList(string css, CssParserOptions? options = null) => Task.FromResult(CssParserApiParser.ParseRuleList(css, options));

    /// <summary>
    /// Parses a single rule (https://wicg.github.io/css-parser-api/#dom-css-parserule,
    /// https://drafts.csswg.org/css-syntax-3/#parse-rule): the task fails with a <c>SyntaxError</c> <see cref="DomException"/>
    /// when the input is empty, is not a valid rule or contains more than a rule.
    /// </summary>
    /// <exception cref="TypeErrorException">A value of <see cref="CssParserOptions.AtRules"/> is not a block production.</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parserule"/>
    [IdlMember("parseRule", "https://wicg.github.io/css-parser-api/#dom-css-parserule")]
    public static Task<CssParserRule> ParseRule(string css, CssParserOptions? options = null)
        => CssParserApiParser.ParseRule(css, options) is { } rule ? Task.FromResult(rule) : Task.FromException<CssParserRule>(new DomException("The input is not a rule", "SyntaxError"));

    /// <summary>
    /// Parses a list of declarations (https://wicg.github.io/css-parser-api/#dom-css-parsedeclarationlist,
    /// https://drafts.csswg.org/css-syntax-3/#parse-list-of-declarations): the <see cref="CssParserDeclaration"/> objects;
    /// the at-rules and the invalid declarations are dropped. The task is completed when the method returns.
    /// </summary>
    /// <exception cref="TypeErrorException">A value of <see cref="CssParserOptions.AtRules"/> is not a block production.</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsedeclarationlist"/>
    [IdlMember("parseDeclarationList", "https://wicg.github.io/css-parser-api/#dom-css-parsedeclarationlist")]
    public static Task<IReadOnlyList<CssParserRule>> ParseDeclarationList(string css, CssParserOptions? options = null) => Task.FromResult(CssParserApiParser.ParseDeclarationList(css, options));

    /// <summary>
    /// Parses a single declaration (https://wicg.github.io/css-parser-api/#dom-css-parsedeclaration,
    /// https://drafts.csswg.org/css-syntax-3/#parse-declaration), e.g. <c>color: red !important</c>.
    /// </summary>
    /// <exception cref="DomException">The input is not a declaration, or contains more than a declaration (e.g. a trailing semicolon) (SyntaxError).</exception>
    /// <exception cref="TypeErrorException">A value of <see cref="CssParserOptions.AtRules"/> is not a block production.</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsedeclaration"/>
    [IdlMember("parseDeclaration", "https://wicg.github.io/css-parser-api/#dom-css-parsedeclaration")]
    public static CssParserDeclaration ParseDeclaration(string css, CssParserOptions? options = null)
        => CssParserApiParser.ParseDeclaration(css, options) ?? throw new DomException("The input is not a declaration", "SyntaxError");

    /// <summary>
    /// Parses a single component value (https://wicg.github.io/css-parser-api/#dom-css-parsevalue,
    /// https://drafts.csswg.org/css-syntax-3/#parse-component-value), surrounded by optional whitespace.
    /// </summary>
    /// <exception cref="DomException">The input is empty or contains more than a component value (SyntaxError).</exception>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsevalue"/>
    [IdlMember("parseValue", "https://wicg.github.io/css-parser-api/#dom-css-parsevalue")]
    public static CssParserToken ParseValue(string css)
        => CssParserApiParser.ParseValue(css) ?? throw new DomException("The input is not a single component value", "SyntaxError");

    /// <summary>
    /// Parses a list of component values (https://wicg.github.io/css-parser-api/#dom-css-parsevaluelist,
    /// https://drafts.csswg.org/css-syntax-3/#parse-list-of-component-values), whitespace included.
    /// </summary>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsevaluelist"/>
    [IdlMember("parseValueList", "https://wicg.github.io/css-parser-api/#dom-css-parsevaluelist")]
    public static IReadOnlyList<CssParserToken> ParseValueList(string css) => CssParserApiParser.ParseValueList(css);

    /// <summary>
    /// Parses a comma-separated list of component values (https://wicg.github.io/css-parser-api/#dom-css-parsecommavaluelist,
    /// https://drafts.csswg.org/css-syntax-3/#parse-comma-separated-list-of-component-values): the component values between
    /// the top-level commas, whitespace included (a trailing comma does not start an empty group).
    /// </summary>
    /// <seealso href="https://wicg.github.io/css-parser-api/#dom-css-parsecommavaluelist"/>
    [IdlMember("parseCommaValueList", "https://wicg.github.io/css-parser-api/#dom-css-parsecommavaluelist")]
    public static IReadOnlyList<IReadOnlyList<CssParserToken>> ParseCommaValueList(string css) => CssParserApiParser.ParseCommaValueList(css);
}
