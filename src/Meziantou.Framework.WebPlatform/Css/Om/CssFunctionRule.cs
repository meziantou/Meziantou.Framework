using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// A parameter of a custom function (https://drafts.csswg.org/css-mixins-1/#function-parameter): its name, its type (the
/// universal syntax definition when it has no type) and its default value, or null.
/// </summary>
internal sealed class CssFunctionParameterDefinition(string name, CssSyntaxDefinition type, string? defaultValue)
{
    public string Name { get; } = name;

    public CssSyntaxDefinition Type { get; } = type;

    public string? DefaultValue { get; } = defaultValue;

    /// <summary>https://drafts.csswg.org/css-mixins-1/#serialize-a-function-parameter</summary>
    public string Serialize()
    {
        var builder = new StringBuilder(CssSerializer.SerializeIdentifier(Name));
        if (!Type.IsUniversal)
        {
            builder.Append(' ').Append(CssFunctionRule.SerializeType(Type));
        }

        if (DefaultValue is not null)
        {
            builder.Append(": ").Append(DefaultValue);
        }

        return builder.ToString();
    }
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#dictdef-functionparameter</summary>
[IdlInterface("FunctionParameter", "https://drafts.csswg.org/css-mixins-1/#dictdef-functionparameter")]
public sealed class CssFunctionParameter
{
    /// <summary>Gets the name of the parameter, including the leading <c>--</c>.</summary>
    [IdlMember("name", "https://drafts.csswg.org/css-mixins-1/#dom-functionparameter-name")]
    public required string Name { get; init; }

    /// <summary>The type of the parameter as a syntax string, or "*" when it has no type.</summary>
    [IdlMember("type", "https://drafts.csswg.org/css-mixins-1/#dom-functionparameter-type")]
    public required string Type { get; init; }

    /// <summary>The default value of the parameter, or null when it has no default value.</summary>
    [IdlMember("defaultValue", "https://drafts.csswg.org/css-mixins-1/#dom-functionparameter-defaultvalue")]
    public string? DefaultValue { get; init; }
}

/// <summary>
/// https://drafts.csswg.org/css-mixins-1/#the-function-rule: a custom function. Its child rules are the runs of declarations
/// of its body (<see cref="CssFunctionDeclarations"/>) and its conditional group rules.
/// </summary>
[IdlInterface("CSSFunctionRule", "https://drafts.csswg.org/css-mixins-1/#cssfunctionrule")]
public sealed class CssFunctionRule : CssGroupingRule
{
    internal CssFunctionRule(string name, List<CssFunctionParameterDefinition> parameters, CssSyntaxDefinition returnType)
    {
        Name = name;
        Parameters = parameters;
        ReturnSyntax = returnType;
    }

    /// <summary>Gets the name of the function, including the leading <c>--</c>.</summary>
    [IdlMember("name", "https://drafts.csswg.org/css-mixins-1/#dom-cssfunctionrule-name")]
    public string Name { get; }

    /// <summary>The return type (the universal syntax definition when the function has no return type).</summary>
    internal CssSyntaxDefinition ReturnSyntax { get; }

    internal List<CssFunctionParameterDefinition> Parameters { get; }

    /// <summary>
    /// Gets the return type of the function as a syntax string, or <c>*</c> when the function has no return type.
    /// </summary>
    [IdlMember("returnType", "https://drafts.csswg.org/css-mixins-1/#dom-cssfunctionrule-returntype")]
    public string ReturnType => ReturnSyntax.Text;

    /// <summary>Returns the parameters of the function.</summary>
    [IdlMember("getParameters", "https://drafts.csswg.org/css-mixins-1/#dom-cssfunctionrule-getparameters")]
    public IReadOnlyList<CssFunctionParameter> GetParameters() => [.. Parameters.Select(p => new CssFunctionParameter { Name = p.Name, Type = p.Type.Text, DefaultValue = p.DefaultValue })];

    /// <summary>The declarations and the child rules are written on the line of the prelude (https://drafts.csswg.org/css-mixins-1/#serialize-a-cssfunctionrule).</summary>
    internal override bool HasMultilineBlock => false;

    internal override string GetPrelude()
    {
        var builder = new StringBuilder("@function ").Append(CssSerializer.SerializeIdentifier(Name)).Append('(');
        builder.AppendJoin(", ", Parameters.Select(p => p.Serialize())).Append(')');
        if (!ReturnSyntax.IsUniversal)
        {
            builder.Append(" returns ").Append(SerializeType(ReturnSyntax));
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/css-mixins-1/#serialize-a-css-type: type() is only written when the syntax has several components.</summary>
    internal static string SerializeType(CssSyntaxDefinition type) => type.ComponentCount == 1 ? type.Text : "type(" + type.Text + ")";
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#cssfunctiondeclarations: a run of consecutive declarations of a @function rule.</summary>
[IdlInterface("CSSFunctionDeclarations", "https://drafts.csswg.org/css-mixins-1/#cssfunctiondeclarations")]
public sealed class CssFunctionDeclarations : CssRule
{
    internal CssFunctionDeclarations(List<CssDeclarationEntry> declarations)
    {
        Style = new CssFunctionDescriptors(this, declarations);
    }

    /// <summary>Gets the declarations of the run.</summary>
    [IdlMember("style", "https://drafts.csswg.org/css-mixins-1/#dom-cssfunctiondeclarations-style")]
    public CssFunctionDescriptors Style { get; }

    /// <summary>Like the nested declarations, only the declarations are written.</summary>
    internal override string GetPrelude() => "";

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-mixins-1/#cssfunctiondescriptors: the local variables (custom properties) and the result descriptor of a @function rule.</summary>
[IdlInterface("CSSFunctionDescriptors", "https://drafts.csswg.org/css-mixins-1/#cssfunctiondescriptors")]
public sealed class CssFunctionDescriptors : CssStyleDeclaration
{
    internal CssFunctionDescriptors(CssRule parentRule, List<CssDeclarationEntry>? declarations)
        : base(parentRule, declarations, FunctionContext)
    {
    }

    /// <summary>Gets or sets the <c>result</c> descriptor of the function body.</summary>
    [IdlMember("result", "https://drafts.csswg.org/css-mixins-1/#dom-cssfunctiondescriptors-result")]
    public string Result
    {
        get => GetPropertyValue("result");
        set => SetProperty("result", value ?? "");
    }
}
