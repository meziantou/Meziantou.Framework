using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// https://drafts.csswg.org/css-anchor-position-1/#om-position-try: a <c>@position-try</c> rule, which defines a position option
/// (https://drafts.csswg.org/css-anchor-position-1/#fallback-rule) with a name, applied to the absolutely positioned boxes that
/// list it in <c>position-try-fallbacks</c>.
/// </summary>
[IdlInterface("CSSPositionTryRule", "https://drafts.csswg.org/css-anchor-position-1/#csspositiontryrule")]
public sealed class CssPositionTryRule : CssRule
{
    internal CssPositionTryRule(string name, List<CssDeclarationEntry> declarations)
    {
        PositionTryName = name;
        Style = new CssPositionTryDescriptors(this, declarations);
    }

    /// <summary>The name of the rule (the value of the &lt;dashed-ident&gt;), referenced by <c>position-try-fallbacks</c>.</summary>
    internal string PositionTryName { get; }

    /// <summary>Gets the name declared in the prelude of the rule (a <c>&lt;dashed-ident&gt;</c>, serialized as an identifier).</summary>
    [IdlMember("name", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontryrule-name")]
    public string Name => CssSerializer.SerializeIdentifier(PositionTryName);

    /// <summary>Gets the declarations of the rule (the accepted @position-try properties, in the specified order).</summary>
    [IdlMember("style", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontryrule-style")]
    public CssPositionTryDescriptors Style { get; }

    internal override string GetPrelude() => "@position-try " + Name;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-anchor-position-1/#csspositiontrydescriptors: the declarations of a <c>@position-try</c> rule.</summary>
[IdlInterface("CSSPositionTryDescriptors", "https://drafts.csswg.org/css-anchor-position-1/#csspositiontrydescriptors")]
public sealed class CssPositionTryDescriptors : CssStyleDeclaration
{
    internal CssPositionTryDescriptors(CssRule parentRule, List<CssDeclarationEntry>? declarations)
        : base(parentRule, declarations, PositionTryContext)
    {
    }

    /// <summary>Gets or sets the <c>margin</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("margin", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin")]
    public string Margin
    {
        get => GetPropertyValue("margin");
        set => SetProperty("margin", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-top</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginTop", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margintop")]
    [IdlMember("margin-top", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-top")]
    public string MarginTop
    {
        get => GetPropertyValue("margin-top");
        set => SetProperty("margin-top", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-right</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginRight", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginright")]
    [IdlMember("margin-right", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-right")]
    public string MarginRight
    {
        get => GetPropertyValue("margin-right");
        set => SetProperty("margin-right", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-bottom</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginBottom", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginbottom")]
    [IdlMember("margin-bottom", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-bottom")]
    public string MarginBottom
    {
        get => GetPropertyValue("margin-bottom");
        set => SetProperty("margin-bottom", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-left</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginLeft", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginleft")]
    [IdlMember("margin-left", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-left")]
    public string MarginLeft
    {
        get => GetPropertyValue("margin-left");
        set => SetProperty("margin-left", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-block</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginBlock", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginblock")]
    [IdlMember("margin-block", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-block")]
    public string MarginBlock
    {
        get => GetPropertyValue("margin-block");
        set => SetProperty("margin-block", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-block-start</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginBlockStart", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginblockstart")]
    [IdlMember("margin-block-start", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-block-start")]
    public string MarginBlockStart
    {
        get => GetPropertyValue("margin-block-start");
        set => SetProperty("margin-block-start", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-block-end</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginBlockEnd", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-marginblockend")]
    [IdlMember("margin-block-end", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-block-end")]
    public string MarginBlockEnd
    {
        get => GetPropertyValue("margin-block-end");
        set => SetProperty("margin-block-end", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-inline</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginInline", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margininline")]
    [IdlMember("margin-inline", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-inline")]
    public string MarginInline
    {
        get => GetPropertyValue("margin-inline");
        set => SetProperty("margin-inline", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-inline-start</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginInlineStart", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margininlinestart")]
    [IdlMember("margin-inline-start", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-inline-start")]
    public string MarginInlineStart
    {
        get => GetPropertyValue("margin-inline-start");
        set => SetProperty("margin-inline-start", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-inline-end</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("marginInlineEnd", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margininlineend")]
    [IdlMember("margin-inline-end", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-margin-inline-end")]
    public string MarginInlineEnd
    {
        get => GetPropertyValue("margin-inline-end");
        set => SetProperty("margin-inline-end", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("inset", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset")]
    public string Inset
    {
        get => GetPropertyValue("inset");
        set => SetProperty("inset", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-block</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetBlock", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetblock")]
    [IdlMember("inset-block", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-block")]
    public string InsetBlock
    {
        get => GetPropertyValue("inset-block");
        set => SetProperty("inset-block", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-block-start</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetBlockStart", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetblockstart")]
    [IdlMember("inset-block-start", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-block-start")]
    public string InsetBlockStart
    {
        get => GetPropertyValue("inset-block-start");
        set => SetProperty("inset-block-start", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-block-end</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetBlockEnd", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetblockend")]
    [IdlMember("inset-block-end", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-block-end")]
    public string InsetBlockEnd
    {
        get => GetPropertyValue("inset-block-end");
        set => SetProperty("inset-block-end", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-inline</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetInline", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetinline")]
    [IdlMember("inset-inline", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-inline")]
    public string InsetInline
    {
        get => GetPropertyValue("inset-inline");
        set => SetProperty("inset-inline", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-inline-start</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetInlineStart", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetinlinestart")]
    [IdlMember("inset-inline-start", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-inline-start")]
    public string InsetInlineStart
    {
        get => GetPropertyValue("inset-inline-start");
        set => SetProperty("inset-inline-start", value ?? "");
    }

    /// <summary>Gets or sets the <c>inset-inline-end</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("insetInlineEnd", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-insetinlineend")]
    [IdlMember("inset-inline-end", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inset-inline-end")]
    public string InsetInlineEnd
    {
        get => GetPropertyValue("inset-inline-end");
        set => SetProperty("inset-inline-end", value ?? "");
    }

    /// <summary>Gets or sets the <c>top</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("top", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-top")]
    public string Top
    {
        get => GetPropertyValue("top");
        set => SetProperty("top", value ?? "");
    }

    /// <summary>Gets or sets the <c>left</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("left", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-left")]
    public string Left
    {
        get => GetPropertyValue("left");
        set => SetProperty("left", value ?? "");
    }

    /// <summary>Gets or sets the <c>right</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("right", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-right")]
    public string Right
    {
        get => GetPropertyValue("right");
        set => SetProperty("right", value ?? "");
    }

    /// <summary>Gets or sets the <c>bottom</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("bottom", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-bottom")]
    public string Bottom
    {
        get => GetPropertyValue("bottom");
        set => SetProperty("bottom", value ?? "");
    }

    /// <summary>Gets or sets the <c>width</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("width", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-width")]
    public string Width
    {
        get => GetPropertyValue("width");
        set => SetProperty("width", value ?? "");
    }

    /// <summary>Gets or sets the <c>min-width</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("minWidth", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-minwidth")]
    [IdlMember("min-width", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-min-width")]
    public string MinWidth
    {
        get => GetPropertyValue("min-width");
        set => SetProperty("min-width", value ?? "");
    }

    /// <summary>Gets or sets the <c>max-width</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("maxWidth", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-maxwidth")]
    [IdlMember("max-width", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-max-width")]
    public string MaxWidth
    {
        get => GetPropertyValue("max-width");
        set => SetProperty("max-width", value ?? "");
    }

    /// <summary>Gets or sets the <c>height</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("height", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-height")]
    public string Height
    {
        get => GetPropertyValue("height");
        set => SetProperty("height", value ?? "");
    }

    /// <summary>Gets or sets the <c>min-height</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("minHeight", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-minheight")]
    [IdlMember("min-height", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-min-height")]
    public string MinHeight
    {
        get => GetPropertyValue("min-height");
        set => SetProperty("min-height", value ?? "");
    }

    /// <summary>Gets or sets the <c>max-height</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("maxHeight", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-maxheight")]
    [IdlMember("max-height", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-max-height")]
    public string MaxHeight
    {
        get => GetPropertyValue("max-height");
        set => SetProperty("max-height", value ?? "");
    }

    /// <summary>Gets or sets the <c>block-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("blockSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-blocksize")]
    [IdlMember("block-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-block-size")]
    public string BlockSize
    {
        get => GetPropertyValue("block-size");
        set => SetProperty("block-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>min-block-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("minBlockSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-minblocksize")]
    [IdlMember("min-block-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-min-block-size")]
    public string MinBlockSize
    {
        get => GetPropertyValue("min-block-size");
        set => SetProperty("min-block-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>max-block-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("maxBlockSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-maxblocksize")]
    [IdlMember("max-block-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-max-block-size")]
    public string MaxBlockSize
    {
        get => GetPropertyValue("max-block-size");
        set => SetProperty("max-block-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>inline-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("inlineSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inlinesize")]
    [IdlMember("inline-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-inline-size")]
    public string InlineSize
    {
        get => GetPropertyValue("inline-size");
        set => SetProperty("inline-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>min-inline-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("minInlineSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-mininlinesize")]
    [IdlMember("min-inline-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-min-inline-size")]
    public string MinInlineSize
    {
        get => GetPropertyValue("min-inline-size");
        set => SetProperty("min-inline-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>max-inline-size</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("maxInlineSize", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-maxinlinesize")]
    [IdlMember("max-inline-size", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-max-inline-size")]
    public string MaxInlineSize
    {
        get => GetPropertyValue("max-inline-size");
        set => SetProperty("max-inline-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>place-self</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("placeSelf", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-placeself")]
    [IdlMember("place-self", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-place-self")]
    public string PlaceSelf
    {
        get => GetPropertyValue("place-self");
        set => SetProperty("place-self", value ?? "");
    }

    /// <summary>Gets or sets the <c>align-self</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("alignSelf", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-alignself")]
    [IdlMember("align-self", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-align-self")]
    public string AlignSelf
    {
        get => GetPropertyValue("align-self");
        set => SetProperty("align-self", value ?? "");
    }

    /// <summary>Gets or sets the <c>justify-self</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("justifySelf", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-justifyself")]
    [IdlMember("justify-self", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-justify-self")]
    public string JustifySelf
    {
        get => GetPropertyValue("justify-self");
        set => SetProperty("justify-self", value ?? "");
    }

    /// <summary>Gets or sets the <c>position-anchor</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("positionAnchor", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-positionanchor")]
    [IdlMember("position-anchor", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-position-anchor")]
    public string PositionAnchor
    {
        get => GetPropertyValue("position-anchor");
        set => SetProperty("position-anchor", value ?? "");
    }

    /// <summary>Gets or sets the <c>position-area</c> property of the rule (an empty string removes the declaration).</summary>
    [IdlMember("positionArea", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-positionarea")]
    [IdlMember("position-area", "https://drafts.csswg.org/css-anchor-position-1/#dom-csspositiontrydescriptors-position-area")]
    public string PositionArea
    {
        get => GetPropertyValue("position-area");
        set => SetProperty("position-area", value ?? "");
    }
}
