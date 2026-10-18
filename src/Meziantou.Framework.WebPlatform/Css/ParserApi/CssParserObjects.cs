using System.Text;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.ParserApi;

/// <summary>
/// The CSSToken typedef: <c>(DOMString or CSSStyleValue or CSSParserValue)</c> (https://wicg.github.io/css-parser-api/#typedefdef-csstoken),
/// a component value (https://drafts.csswg.org/css-syntax-3/#component-value) of the results of the CSS Parser API. The
/// parser represents an ident token as a <see cref="CssKeywordValue"/>, a number, a percentage or a dimension token whose
/// unit is a CSS unit as a <see cref="CssUnitValue"/>, a function as a <see cref="CssParserFunction"/>, a simple block as a
/// <see cref="CssParserBlock"/>, a whitespace token as <c>" "</c>, and the other tokens as their text (e.g. <c>","</c>,
/// <c>"#fff"</c>, <c>"\"text\""</c>, <c>"10foo"</c>).
/// </summary>
public readonly struct CssParserToken : IUnionValue, IEquatable<CssParserToken>
{
    private CssParserToken(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/>, a <see cref="CssStyleValue"/> or a <see cref="CssParserValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the token as a string, or null when it is an object.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Gets the token as a <see cref="CssStyleValue"/>, or null.</summary>
    [NotInIdl]
    public CssStyleValue? StyleValue => Value as CssStyleValue;

    /// <summary>Gets the token as a <see cref="CssParserValue"/> (a function or a block), or null.</summary>
    [NotInIdl]
    public CssParserValue? ParserValue => Value as CssParserValue;

    /// <summary>Converts a <c>string</c> to a <c>CssParserToken</c>.</summary>
    [NotInIdl]
    public static implicit operator CssParserToken(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssStyleValue</c> to a <c>CssParserToken</c>.</summary>
    [NotInIdl]
    public static implicit operator CssParserToken(CssStyleValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssParserValue</c> to a <c>CssParserToken</c>.</summary>
    [NotInIdl]
    public static implicit operator CssParserToken(CssParserValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssParserToken</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssParserToken FromString(string value) => value;

    /// <summary>Creates a <c>CssParserToken</c> from a <c>CssStyleValue</c>.</summary>
    [NotInIdl]
    public static CssParserToken FromCssStyleValue(CssStyleValue value) => value;

    /// <summary>Creates a <c>CssParserToken</c> from a <c>CssParserValue</c>.</summary>
    [NotInIdl]
    public static CssParserToken FromCssParserValue(CssParserValue value) => value;

    /// <summary>Returns the serialization of the token.</summary>
    [NotInIdl]
    public override string ToString() => Value?.ToString() ?? "";

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssParserToken other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssParserToken other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssParserToken</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssParserToken left, CssParserToken right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssParserToken</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssParserToken left, CssParserToken right) => !left.Equals(right);
}

/// <summary>
/// The options of the CSS Parser API (https://wicg.github.io/css-parser-api/#dictdef-cssparseroptions).
/// </summary>
[IdlInterface("CSSParserOptions", "https://wicg.github.io/css-parser-api/#dictdef-cssparseroptions")]
public sealed class CssParserOptions
{
    /// <summary>
    /// Gets the grammar of the blocks of the at-rules, by at-rule name (without <c>@</c>, ASCII case-insensitive): one of the
    /// productions of CSS Syntax (https://drafts.csswg.org/css-syntax-3/#block-contents), <c>"block-contents"</c> (the
    /// default: declarations and rules), <c>"declaration-list"</c>, <c>"qualified-rule-list"</c>, <c>"at-rule-list"</c>,
    /// <c>"declaration-rule-list"</c> or <c>"rule-list"</c>. The items of a block that its production does not allow are
    /// dropped (e.g. the declarations of a <c>"rule-list"</c>). The draft describes the option as a "dict of at-rule name =&gt;
    /// at-rule type" without defining the types: the bindings take a <c>record&lt;DOMString, DOMString&gt;</c>.
    /// </summary>
    [IdlMember("atRules", "https://wicg.github.io/css-parser-api/#dom-cssparseroptions-atrules")]
    public IEnumerable<KeyValuePair<string, string>>? AtRules { get; init; }
}

/// <summary>A rule or a declaration returned by the CSS Parser API (https://wicg.github.io/css-parser-api/#cssparserrule).</summary>
[IdlInterface("CSSParserRule", "https://wicg.github.io/css-parser-api/#cssparserrule")]
public abstract class CssParserRule
{
    private protected CssParserRule()
    {
    }

    internal abstract void Serialize(StringBuilder builder);

    /// <summary>Returns the serialization of the rule.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Serialize(builder);
        return builder.ToString();
    }

    /// <summary>Appends the items of a block between braces, each declaration followed by a semicolon.</summary>
    internal static void SerializeBody(StringBuilder builder, IReadOnlyList<CssParserRule> body)
    {
        builder.Append('{');
        foreach (var rule in body)
        {
            builder.Append(' ');
            rule.Serialize(builder);
            if (rule is CssParserDeclaration)
            {
                builder.Append(';');
            }
        }

        builder.Append(body.Count > 0 ? " }" : "}");
    }
}

/// <summary>
/// An at-rule (https://wicg.github.io/css-parser-api/#cssparseratrule, https://drafts.csswg.org/css-syntax-3/#at-rule):
/// its name, its prelude and its block, or no block for a statement (e.g. <c>@import url(a.css);</c>).
/// </summary>
[IdlInterface("CSSParserAtRule", "https://wicg.github.io/css-parser-api/#cssparseratrule")]
public sealed class CssParserAtRule : CssParserRule
{
    /// <summary>Creates an at-rule (https://wicg.github.io/css-parser-api/#dom-cssparseratrule-cssparseratrule).</summary>
    /// <param name="name">The name, without <c>@</c>.</param>
    /// <param name="prelude">The component values between the name and the block.</param>
    /// <param name="body">The rules and declarations of the block, or null for a statement.</param>
    [IdlMember("constructor", "https://wicg.github.io/css-parser-api/#dom-cssparseratrule-cssparseratrule")]
    public CssParserAtRule(string name, IEnumerable<CssParserToken> prelude, IEnumerable<CssParserRule>? body = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(prelude);
        Name = name;
        Prelude = prelude.ToArray().AsReadOnly();
        Body = body?.ToArray().AsReadOnly();
    }

    /// <summary>Gets the name of the at-rule, without <c>@</c>.</summary>
    [IdlMember("name", "https://wicg.github.io/css-parser-api/#dom-cssparseratrule-name")]
    public string Name { get; }

    /// <summary>Gets the component values between the name and the block (or the semicolon), whitespace included.</summary>
    [IdlMember("prelude", "https://wicg.github.io/css-parser-api/#dom-cssparseratrule-prelude")]
    public IReadOnlyList<CssParserToken> Prelude { get; }

    /// <summary>Gets the rules and declarations of the block, or null when the at-rule has no block.</summary>
    [IdlMember("body", "https://wicg.github.io/css-parser-api/#dom-cssparseratrule-body")]
    public IReadOnlyList<CssParserRule>? Body { get; }

    internal override void Serialize(StringBuilder builder)
    {
        builder.Append('@').Append(Values.CssSerializer.SerializeIdentifier(Name));
        CssParserValue.SerializeTokens(builder, Prelude);
        if (Body is null)
        {
            builder.Append(';');
        }
        else
        {
            SerializeBody(builder, Body);
        }
    }
}

/// <summary>
/// A qualified rule (https://wicg.github.io/css-parser-api/#cssparserqualifiedrule, https://drafts.csswg.org/css-syntax-3/#qualified-rule),
/// e.g. a style rule: its prelude and the rules and declarations of its block.
/// </summary>
[IdlInterface("CSSParserQualifiedRule", "https://wicg.github.io/css-parser-api/#cssparserqualifiedrule")]
public sealed class CssParserQualifiedRule : CssParserRule
{
    /// <summary>Creates a qualified rule (https://wicg.github.io/css-parser-api/#dom-cssparserqualifiedrule-cssparserqualifiedrule).</summary>
    /// <param name="prelude">The component values before the block (e.g. the selectors).</param>
    /// <param name="body">The rules and declarations of the block (null is an empty block).</param>
    [IdlMember("constructor", "https://wicg.github.io/css-parser-api/#dom-cssparserqualifiedrule-cssparserqualifiedrule")]
    public CssParserQualifiedRule(IEnumerable<CssParserToken> prelude, IEnumerable<CssParserRule>? body = null)
    {
        ArgumentNullException.ThrowIfNull(prelude);
        Prelude = prelude.ToArray().AsReadOnly();
        Body = (body?.ToArray() ?? []).AsReadOnly();
    }

    /// <summary>Gets the component values before the block, whitespace included.</summary>
    [IdlMember("prelude", "https://wicg.github.io/css-parser-api/#dom-cssparserqualifiedrule-prelude")]
    public IReadOnlyList<CssParserToken> Prelude { get; }

    /// <summary>Gets the rules and declarations of the block.</summary>
    [IdlMember("body", "https://wicg.github.io/css-parser-api/#dom-cssparserqualifiedrule-body")]
    public IReadOnlyList<CssParserRule> Body { get; }

    internal override void Serialize(StringBuilder builder)
    {
        CssParserValue.SerializeTokens(builder, Prelude);
        SerializeBody(builder, Body);
    }
}

/// <summary>
/// A declaration (https://wicg.github.io/css-parser-api/#cssparserdeclaration, https://drafts.csswg.org/css-syntax-3/#declaration):
/// a name and a value, e.g. <c>color: red</c>.
/// </summary>
[IdlInterface("CSSParserDeclaration", "https://wicg.github.io/css-parser-api/#cssparserdeclaration")]
public sealed class CssParserDeclaration : CssParserRule
{
    /// <summary>Creates a declaration (https://wicg.github.io/css-parser-api/#dom-cssparserdeclaration-cssparserdeclaration).</summary>
    /// <param name="name">The name of the declaration (e.g. a property name).</param>
    /// <param name="body">The component values of the value. The IDL of the draft declares a sequence of rules; as the
    /// <see cref="Body"/> attribute, it is a list of component values.</param>
    [IdlMember("constructor", "https://wicg.github.io/css-parser-api/#dom-cssparserdeclaration-cssparserdeclaration")]
    public CssParserDeclaration(string name, IEnumerable<CssParserToken>? body = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Body = (body?.ToArray() ?? []).AsReadOnly();
    }

    /// <summary>Gets the name of the declaration.</summary>
    [IdlMember("name", "https://wicg.github.io/css-parser-api/#dom-cssparserdeclaration-name")]
    public string Name { get; }

    /// <summary>
    /// Gets the component values of the value, without the leading and trailing whitespace and without <c>!important</c>
    /// (https://drafts.csswg.org/css-syntax-3/#consume-declaration).
    /// </summary>
    [IdlMember("body", "https://wicg.github.io/css-parser-api/#dom-cssparserdeclaration-body")]
    public IReadOnlyList<CssParserToken> Body { get; }

    /// <summary>
    /// Gets a value indicating whether the value ended with <c>!important</c> (the important flag of CSS Syntax). The draft
    /// has no attribute for it: the scripts see it in the serialization.
    /// </summary>
    [NotInIdl("The important flag of CSS Syntax, which the IDL of the draft does not expose")]
    public bool Important { get; init; }

    internal override void Serialize(StringBuilder builder)
    {
        builder.Append(Values.CssSerializer.SerializeIdentifier(Name)).Append(':');
        if (Body.Count > 0)
        {
            builder.Append(' ');
            CssParserValue.SerializeTokens(builder, Body);
        }

        if (Important)
        {
            builder.Append(" !important");
        }
    }
}

/// <summary>A function or a simple block returned by the CSS Parser API (https://wicg.github.io/css-parser-api/#cssparservalue).</summary>
[IdlInterface("CSSParserValue", "https://wicg.github.io/css-parser-api/#cssparservalue")]
public abstract class CssParserValue
{
    private protected CssParserValue()
    {
    }

    internal abstract void Serialize(StringBuilder builder);

    /// <summary>Returns the serialization of the value.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Serialize(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Appends the serialization of component values; an empty comment separates two tokens that would otherwise be parsed as
    /// one (https://drafts.csswg.org/css-syntax-3/#serialization).
    /// </summary>
    internal static void SerializeTokens(StringBuilder builder, IReadOnlyList<CssParserToken> tokens)
    {
        var start = builder.Length;
        foreach (var token in tokens)
        {
            var text = token.ToString();
            if (builder.Length > start && text.Length > 0 && IsNameOrNumberCharacter(builder[^1]) && (IsNameOrNumberCharacter(text[0]) || text[0] == '%'))
            {
                builder.Append("/**/");
            }

            builder.Append(text);
        }
    }

    private static bool IsNameOrNumberCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' || c >= 0x80;
}

/// <summary>
/// A simple block (https://wicg.github.io/css-parser-api/#cssparserblock, https://drafts.csswg.org/css-syntax-3/#simple-block):
/// <c>[...]</c>, <c>(...)</c> or <c>{...}</c>.
/// </summary>
[IdlInterface("CSSParserBlock", "https://wicg.github.io/css-parser-api/#cssparserblock")]
public sealed class CssParserBlock : CssParserValue
{
    /// <summary>Creates a simple block (https://wicg.github.io/css-parser-api/#dom-cssparserblock-cssparserblock).</summary>
    /// <param name="name">The brackets of the block: <c>"[]"</c>, <c>"()"</c> or <c>"{}"</c>.</param>
    /// <param name="body">The component values of the block.</param>
    /// <exception cref="TypeErrorException">The name is not <c>"[]"</c>, <c>"()"</c> or <c>"{}"</c>.</exception>
    [IdlMember("constructor", "https://wicg.github.io/css-parser-api/#dom-cssparserblock-cssparserblock")]
    public CssParserBlock(string name, IEnumerable<CssParserToken> body)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);
        if (name is not ("[]" or "()" or "{}"))
            throw new TypeErrorException($"'{name}' is not a block name: it must be \"[]\", \"()\" or \"{{}}\"");

        Name = name;
        Body = body.ToArray().AsReadOnly();
    }

    /// <summary>Gets the brackets of the block: <c>"[]"</c>, <c>"()"</c> or <c>"{}"</c>.</summary>
    [IdlMember("name", "https://wicg.github.io/css-parser-api/#dom-cssparserblock-name")]
    public string Name { get; }

    /// <summary>Gets the component values of the block.</summary>
    [IdlMember("body", "https://wicg.github.io/css-parser-api/#dom-cssparserblock-body")]
    public IReadOnlyList<CssParserToken> Body { get; }

    internal override void Serialize(StringBuilder builder)
    {
        builder.Append(Name[0]);
        SerializeTokens(builder, Body);
        builder.Append(Name[1]);
    }
}

/// <summary>
/// A function (https://wicg.github.io/css-parser-api/#cssparserfunction, https://drafts.csswg.org/css-syntax-3/#function): its
/// name and its arguments, the component values between the commas at the top level of the function.
/// </summary>
[IdlInterface("CSSParserFunction", "https://wicg.github.io/css-parser-api/#cssparserfunction")]
public sealed class CssParserFunction : CssParserValue
{
    /// <summary>Creates a function (https://wicg.github.io/css-parser-api/#dom-cssparserfunction-cssparserfunction).</summary>
    /// <param name="name">The name of the function, without the parenthesis.</param>
    /// <param name="args">The arguments: the component values between the commas.</param>
    [IdlMember("constructor", "https://wicg.github.io/css-parser-api/#dom-cssparserfunction-cssparserfunction")]
    public CssParserFunction(string name, IEnumerable<IEnumerable<CssParserToken>> args)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);
        Name = name;
        Args = args.Select(IReadOnlyList<CssParserToken> (arg) => (arg ?? throw new ArgumentException("An argument is null", nameof(args))).ToArray().AsReadOnly()).ToArray().AsReadOnly();
    }

    /// <summary>Gets the name of the function, without the parenthesis.</summary>
    [IdlMember("name", "https://wicg.github.io/css-parser-api/#dom-cssparserfunction-name")]
    public string Name { get; }

    /// <summary>
    /// Gets the arguments: the component values between the commas at the top level of the function (n commas separate n + 1
    /// arguments; a function without component values has no argument).
    /// </summary>
    [IdlMember("args", "https://wicg.github.io/css-parser-api/#dom-cssparserfunction-args")]
    public IReadOnlyList<IReadOnlyList<CssParserToken>> Args { get; }

    internal override void Serialize(StringBuilder builder)
    {
        builder.Append(Values.CssSerializer.SerializeIdentifier(Name)).Append('(');
        for (var i = 0; i < Args.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            SerializeTokens(builder, Args[i]);
        }

        builder.Append(')');
    }
}
