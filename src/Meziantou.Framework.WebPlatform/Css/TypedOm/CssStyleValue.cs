using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>
/// A CSS value of the Typed OM (https://drafts.css-houdini.org/css-typed-om-1/#cssstylevalue). The values that have no more
/// specialized subclass are direct <see cref="CssStyleValue"/> objects: they are opaque, and are only valid for the property
/// they were reified for (https://drafts.css-houdini.org/css-typed-om-1/#reify-as-a-cssstylevalue).
/// </summary>
[IdlInterface("CSSStyleValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssstylevalue")]
public class CssStyleValue
{
    private readonly string _cssText = "";

    private protected CssStyleValue()
    {
    }

    /// <summary>Creates a direct CSSStyleValue that represents a value of a property (https://drafts.css-houdini.org/css-typed-om-1/#reify-as-a-cssstylevalue).</summary>
    internal CssStyleValue(string associatedProperty, string cssText)
    {
        AssociatedProperty = associatedProperty;
        _cssText = cssText;
    }

    /// <summary>The property the value was reified for, or null (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylevalue-associatedproperty-slot).</summary>
    internal string? AssociatedProperty { get; }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylevalue-parse</summary>
    [IdlMember("parse", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylevalue-parse")]
    public static CssStyleValue Parse(string property, string cssText) => CssStyleValueReifier.Parse(property, cssText, parseMultiple: false)[0];

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylevalue-parseall</summary>
    [IdlMember("parseAll", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylevalue-parseall")]
    public static IReadOnlyList<CssStyleValue> ParseAll(string property, string cssText) => CssStyleValueReifier.Parse(property, cssText, parseMultiple: true);

    /// <summary>The serialization of the value (https://drafts.css-houdini.org/css-typed-om-1/#stylevalue-serialization).</summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => _cssText;
}

/// <summary>A keyword or an identifier (https://drafts.css-houdini.org/css-typed-om-1/#csskeywordvalue).</summary>
[IdlInterface("CSSKeywordValue", "https://drafts.css-houdini.org/css-typed-om-1/#csskeywordvalue")]
public sealed class CssKeywordValue : CssStyleValue
{
    private string _value;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csskeywordvalue-csskeywordvalue</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csskeywordvalue-csskeywordvalue")]
    public CssKeywordValue(string value)
    {
        _value = Validate(value);
    }

    /// <summary>Gets or sets the keyword; it must not be empty.</summary>
    [IdlMember("value", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csskeywordvalue-value")]
    public string Value
    {
        get => _value;
        set => _value = Validate(value);
    }

    private static string Validate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            throw new TypeErrorException("The keyword must not be empty");

        return value;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#keywordvalue-serialization: the value, serialized as an identifier.</summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => CssSerializer.SerializeIdentifier(_value);
}

/// <summary>
/// An &lt;image&gt; value (https://drafts.css-houdini.org/css-typed-om-1/#cssimagevalue). The object is opaque: it is only
/// a value that can be set to a property that accepts images.
/// </summary>
[IdlInterface("CSSImageValue", "https://drafts.css-houdini.org/css-typed-om-1/#cssimagevalue")]
public sealed class CssImageValue : CssStyleValue
{
    private readonly string _cssText;

    internal CssImageValue(string cssText)
    {
        _cssText = cssText;
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => _cssText;
}
