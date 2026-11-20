using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// https://drafts.csswg.org/css-mixins-1/#mixin-rule: a mixin. Its body (https://drafts.csswg.org/css-mixins-1/#mixin-body) is
/// parsed as a nested declarations rule: its declarations (written as <see cref="CssNestedDeclarations"/> rules), its nested
/// style rules, its conditional group rules, its @apply, @contents and @private rules replace the @apply rules that apply the
/// mixin. @layer rules are invalid in the body.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-mixins-1/#cssmixinrule"/>
[IdlInterface("CSSMixinRule", "https://drafts.csswg.org/css-mixins-1/#cssmixinrule")]
public sealed class CssMixinRule : CssGroupingRule
{
    internal CssMixinRule(string name, List<CssFunctionParameterDefinition> parameters)
    {
        Name = name;
        Parameters = parameters;
    }

    /// <summary>Gets the name of the mixin, including the leading <c>--</c>.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-name")]
    public string Name { get; }

    internal List<CssFunctionParameterDefinition> Parameters { get; }

    /// <summary>Returns the parameters of the mixin.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-getparameters"/>
    [IdlMember("getParameters", "https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-getparameters")]
    public IReadOnlyList<CssFunctionParameter> GetParameters() => [.. Parameters.Select(p => new CssFunctionParameter { Name = p.Name, Type = p.Type.Text, DefaultValue = p.DefaultValue })];

    /// <summary>Whether the mixin accepts a contents block: its body contains a @contents rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-contents"/>
    [IdlMember("contents", "https://drafts.csswg.org/css-mixins-1/#dom-cssmixinrule-contents")]
    public bool Contents => ContainsContentsRule(ChildRules);

    /// <summary>https://drafts.csswg.org/css-mixins-1/#serialize-a-cssmixinrule: the parentheses are only written when the mixin has parameters.</summary>
    internal override string GetPrelude()
    {
        var builder = new StringBuilder("@mixin ").Append(CssSerializer.SerializeIdentifier(Name));
        if (Parameters.Count > 0)
        {
            builder.Append('(').AppendJoin(", ", Parameters.Select(p => p.Serialize())).Append(')');
        }

        return builder.ToString();
    }

    private static bool ContainsContentsRule(List<CssRule>? rules)
    {
        if (rules is null)
            return false;

        foreach (var rule in rules)
        {
            if (rule is CssContentsStatementRule or CssContentsBlockRule || ContainsContentsRule(rule.ChildRules))
                return true;
        }

        return false;
    }
}

/// <summary>
/// The name and the arguments of an @apply rule (https://drafts.csswg.org/css-mixins-1/#apply-rule):
/// @apply [ &lt;dashed-ident&gt; | &lt;dashed-function&gt; ] [ { &lt;declaration-list&gt; } ]?.
/// </summary>
internal sealed class CssApplyPrelude(string name, List<string> arguments)
{
    public string Name { get; } = name;

    /// <summary>The arguments, serialized as the specified value of a custom property.</summary>
    public List<string> Arguments { get; } = arguments;

    /// <summary>https://drafts.csswg.org/css-mixins-1/#serialize-an-apply-prelude</summary>
    public string Serialize()
    {
        var builder = new StringBuilder("@apply ").Append(CssSerializer.SerializeIdentifier(Name));
        if (Arguments.Count > 0)
        {
            builder.Append('(').AppendJoin(", ", Arguments.Select(SerializeArgument)).Append(')');
        }

        return builder.ToString();
    }

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#serialize-a-function-argument: an argument containing a top-level comma or {}
    /// block is written in a {} block. The blocks left open at the end of the style sheet are closed, so that they do not
    /// contain the closing characters of the prelude.
    /// </summary>
    private static string SerializeArgument(string argument)
    {
        argument = CssStyleDeclaration.CloseUnclosedBlocks(argument);
        var values = CssParser.ParseListOfComponentValues(argument);
        return values.Exists(v => v is CssPreservedToken { Type: CssTokenType.Comma } or CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket }) ? "{ " + argument + " }" : argument;
    }
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#cssapplyblockrule: an @apply rule with a contents block, whose rules are the child rules.</summary>
/// <seealso href="https://drafts.csswg.org/css-mixins-1/#cssapplyblockrule"/>
[IdlInterface("CSSApplyBlockRule", "https://drafts.csswg.org/css-mixins-1/#cssapplyblockrule")]
public sealed class CssApplyBlockRule : CssGroupingRule
{
    internal CssApplyBlockRule(CssApplyPrelude prelude)
    {
        Prelude = prelude;
    }

    internal CssApplyPrelude Prelude { get; }

    /// <summary>Gets the name of the mixin applied by the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssapplyblockrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-mixins-1/#dom-cssapplyblockrule-name")]
    public string Name => CssSerializer.SerializeIdentifier(Prelude.Name);

    /// <summary>Returns the arguments of the rule as serialized text.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssapplyblockrule-getarguments"/>
    [IdlMember("getArguments", "https://drafts.csswg.org/css-mixins-1/#dom-cssapplyblockrule-getarguments")]
    public IReadOnlyList<string> GetArguments() => [.. Prelude.Arguments];

    /// <summary>The contents block is written on the line of the prelude (https://drafts.csswg.org/css-mixins-1/#serialize-a-cssapplyblockrule).</summary>
    internal override bool HasMultilineBlock => false;

    internal override string GetPrelude() => Prelude.Serialize();
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#cssapplystatementrule: an @apply rule without a contents block.</summary>
/// <seealso href="https://drafts.csswg.org/css-mixins-1/#cssapplystatementrule"/>
[IdlInterface("CSSApplyStatementRule", "https://drafts.csswg.org/css-mixins-1/#cssapplystatementrule")]
public sealed class CssApplyStatementRule : CssRule
{
    internal CssApplyStatementRule(CssApplyPrelude prelude)
    {
        Prelude = prelude;
    }

    internal CssApplyPrelude Prelude { get; }

    /// <summary>Gets the name of the mixin applied by the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssapplystatementrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-mixins-1/#dom-cssapplystatementrule-name")]
    public string Name => CssSerializer.SerializeIdentifier(Prelude.Name);

    /// <summary>Returns the arguments of the rule as serialized text.</summary>
    /// <seealso href="https://drafts.csswg.org/css-mixins-1/#dom-cssapplystatementrule-getarguments"/>
    [IdlMember("getArguments", "https://drafts.csswg.org/css-mixins-1/#dom-cssapplystatementrule-getarguments")]
    public IReadOnlyList<string> GetArguments() => [.. Prelude.Arguments];

    internal override bool IsStatement => true;

    internal override string GetPrelude() => Prelude.Serialize();
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#csscontentsblockrule: a @contents rule with a fallback block, whose rules are the child rules.</summary>
/// <seealso href="https://drafts.csswg.org/css-mixins-1/#csscontentsblockrule"/>
[IdlInterface("CSSContentsBlockRule", "https://drafts.csswg.org/css-mixins-1/#csscontentsblockrule")]
public sealed class CssContentsBlockRule : CssGroupingRule
{
    internal CssContentsBlockRule()
    {
    }

    internal override bool HasMultilineBlock => false;

    internal override string GetPrelude() => "@contents";
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#csscontentsstatementrule: a @contents rule without a fallback block.</summary>
/// <seealso href="https://drafts.csswg.org/css-mixins-1/#csscontentsstatementrule"/>
[IdlInterface("CSSContentsStatementRule", "https://drafts.csswg.org/css-mixins-1/#csscontentsstatementrule")]
public sealed class CssContentsStatementRule : CssRule
{
    internal CssContentsStatementRule()
    {
    }

    internal override bool IsStatement => true;

    internal override string GetPrelude() => "@contents";
}

/// <summary>A declaration of a @private rule: a custom property name, an optional type and a value.</summary>
internal sealed record CssPrivateDeclaration(string Name, CssSyntaxDefinition Type, string Value);

/// <summary>
/// https://drafts.csswg.org/css-mixins-1/#private: @private { &lt;declaration-rule-list&gt; } defines the private
/// properties of the style rule (or of the mixin body) that contains it: &lt;custom-property-name&gt; &lt;css-type&gt;? :
/// &lt;declaration-value&gt;.
/// </summary>
/// <seealso href="https://github.com/w3c/csswg-drafts/issues/14243#issuecomment-5180782117"/>
[IdlInterface("CSSPrivateRule", "https://github.com/w3c/csswg-drafts/issues/14243#issuecomment-5180782117")]
[Experimental(ExperimentalApis.CssPrivateRule, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CssPrivateRule : CssRule
{
    internal CssPrivateRule(List<CssPrivateDeclaration> declarations)
    {
        Declarations = declarations;
    }

    internal List<CssPrivateDeclaration> Declarations { get; }

    internal override string GetPrelude() => "@private";

    /// <summary>A declaration is written with its type, if any: <c>--name &lt;type&gt;: value;</c>.</summary>
    internal override List<CssSerializedDeclaration> GetDeclarations() =>
        [.. Declarations.Select(d => new CssSerializedDeclaration(d.Type.IsUniversal ? CssSerializer.SerializeIdentifier(d.Name) : CssSerializer.SerializeIdentifier(d.Name) + " " + CssFunctionRule.SerializeType(d.Type), d.Value, Important: false))];
}
