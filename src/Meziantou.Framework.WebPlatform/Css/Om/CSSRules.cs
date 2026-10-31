using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>https://drafts.csswg.org/cssom/#the-cssrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssrule"/>
[IdlInterface("CSSRule", "https://drafts.csswg.org/cssom/#cssrule")]
public abstract partial class CssRule
{
    /// <summary>The value of <see cref="Type"/> for style rules (<c>STYLE_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-style_rule"/>
    [IdlMember("STYLE_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-style_rule")]
    public const ushort StyleRule = 1;

    /// <summary>The value of <see cref="Type"/> for <c>@charset</c> rules (<c>CHARSET_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-charset_rule"/>
    [IdlMember("CHARSET_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-charset_rule")]
    public const ushort CharsetRule = 2;

    /// <summary>The value of <see cref="Type"/> for <c>@import</c> rules (<c>IMPORT_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-import_rule"/>
    [IdlMember("IMPORT_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-import_rule")]
    public const ushort ImportRule = 3;

    /// <summary>The value of <see cref="Type"/> for <c>@media</c> rules (<c>MEDIA_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-media_rule"/>
    [IdlMember("MEDIA_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-media_rule")]
    public const ushort MediaRule = 4;

    /// <summary>The value of <see cref="Type"/> for <c>@font-face</c> rules (<c>FONT_FACE_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-font_face_rule"/>
    [IdlMember("FONT_FACE_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-font_face_rule")]
    public const ushort FontFaceRule = 5;

    /// <summary>The value of <see cref="Type"/> for <c>@page</c> rules (<c>PAGE_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-page_rule"/>
    [IdlMember("PAGE_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-page_rule")]
    public const ushort PageRule = 6;

    /// <summary>The value of <see cref="Type"/> for <c>@keyframes</c> rules (<c>KEYFRAMES_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-cssrule-keyframes_rule"/>
    [IdlMember("KEYFRAMES_RULE", "https://drafts.csswg.org/css-animations-1/#dom-cssrule-keyframes_rule")]
    public const ushort KeyframesRule = 7;

    /// <summary>The value of <see cref="Type"/> for keyframe rules (<c>KEYFRAME_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-cssrule-keyframe_rule"/>
    [IdlMember("KEYFRAME_RULE", "https://drafts.csswg.org/css-animations-1/#dom-cssrule-keyframe_rule")]
    public const ushort KeyframeRule = 8;

    /// <summary>The value of <see cref="Type"/> for page margin rules (<c>MARGIN_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-margin_rule"/>
    [IdlMember("MARGIN_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-margin_rule")]
    public const ushort MarginRule = 9;

    /// <summary>The value of <see cref="Type"/> for <c>@namespace</c> rules (<c>NAMESPACE_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-namespace_rule"/>
    [IdlMember("NAMESPACE_RULE", "https://drafts.csswg.org/cssom/#dom-cssrule-namespace_rule")]
    public const ushort NamespaceRule = 10;

    /// <summary>The value of <see cref="Type"/> for <c>@counter-style</c> rules (<c>COUNTER_STYLE_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-cssrule-counter_style_rule"/>
    [IdlMember("COUNTER_STYLE_RULE", "https://drafts.csswg.org/css-counter-styles-3/#dom-cssrule-counter_style_rule")]
    public const ushort CounterStyleRule = 11;

    /// <summary>The value of <see cref="Type"/> for <c>@supports</c> rules (<c>SUPPORTS_RULE</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-3/#dom-cssrule-supports_rule"/>
    [IdlMember("SUPPORTS_RULE", "https://drafts.csswg.org/css-conditional-3/#dom-cssrule-supports_rule")]
    public const ushort SupportsRule = 12;

    /// <summary>
    /// The value of <see cref="Type"/> for <c>@font-feature-values</c> rules (<c>FONT_FEATURE_VALUES_RULE</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-4/#dom-cssrule-font_feature_values_rule"/>
    [IdlMember("FONT_FEATURE_VALUES_RULE", "https://drafts.csswg.org/css-fonts-4/#dom-cssrule-font_feature_values_rule")]
    public const ushort FontFeatureValuesRule = 14;

    private protected CssRule()
    {
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#dom-cssrule-csstext: the getter serializes the rule; the setter does nothing.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-csstext"/>
    [IdlMember("cssText", "https://drafts.csswg.org/cssom/#dom-cssrule-csstext")]
    public string CssText
    {
        get => Serialize();
        set => _ = value;
    }

    /// <summary>
    /// Gets the rule that contains this rule, or null when the rule is at the top level of a style sheet.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-parentrule"/>
    [IdlMember("parentRule", "https://drafts.csswg.org/cssom/#dom-cssrule-parentrule")]
    public CssRule? ParentRule { get; internal set; }

    /// <summary>Gets the style sheet that contains the rule, or null when the rule is not in a style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-parentstylesheet"/>
    [IdlMember("parentStyleSheet", "https://drafts.csswg.org/cssom/#dom-cssrule-parentstylesheet")]
    public CssStyleSheet? ParentStyleSheet { get; private set; }

    /// <summary>https://drafts.csswg.org/cssom/#dom-cssrule-type: 0 for the rule types that have no legacy type.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrule-type"/>
    [IdlMember("type", "https://drafts.csswg.org/cssom/#dom-cssrule-type")]
    public virtual ushort Type => 0;

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-css-rule</summary>
    internal string Serialize() => CssFormatter.Default.Format(this);

    /// <summary>
    /// The text written before the block of the rule, or before the semicolon of a statement at-rule: the selectors of a
    /// style rule, the key text of a keyframe, the at-keyword and the prelude of an at-rule.
    /// </summary>
    internal abstract string GetPrelude();

    /// <summary>Whether the rule is a statement at-rule, written as its prelude followed by a semicolon (@import, @namespace, @layer statement).</summary>
    internal virtual bool IsStatement => false;

    /// <summary>
    /// Whether the CSSOM serialization writes the items of the block of the rule on their own line (the rules that contain
    /// rules), instead of on the line of the prelude (the rules that only contain declarations).
    /// </summary>
    internal virtual bool HasMultilineBlock => false;

    /// <summary>The declarations or descriptors of the block of the rule, as serialized by https://drafts.csswg.org/cssom/#serialize-a-css-declaration-block.</summary>
    internal virtual List<CssSerializedDeclaration> GetDeclarations() => [];

    /// <summary>The child rules (of grouping rules and @keyframes).</summary>
    internal virtual List<CssRule>? ChildRules => null;

    /// <summary>Sets the parent style sheet of the rule and of its descendants.</summary>
    internal void SetParentStyleSheet(CssStyleSheet? sheet)
    {
        ParentStyleSheet = sheet;
        if (ChildRules is { } children)
        {
            foreach (var child in children)
            {
                child.SetParentStyleSheet(sheet);
            }
        }
    }

    /// <summary>Returns the environment the media queries of the rule are evaluated against: the one of the document of its style sheet.</summary>
    internal MediaEnvironment GetMediaEnvironment()
    {
        var document = ParentStyleSheet?.RootStyleSheet is { } sheet ? sheet.OwnerNode?.NodeDocument ?? sheet.ConstructorDocument : null;
        return document is null ? MediaEnvironment.Default : Cascade.StyleEngine.CreateEnvironment(document);
    }

    /// <summary>Returns whether the rule is a style rule or is nested in a style rule (the nested flag of the CSSOM algorithms).</summary>
    internal bool IsNestedInStyleRule()
    {
        for (var rule = this; rule is not null; rule = rule.ParentRule)
        {
            if (rule is CssStyleRule or CssScopeRule or CssMixinResultRule or CssApplyBlockRule or CssContentsBlockRule)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the rule is a @mixin rule or is in the body of one, outside of a @result rule (https://drafts.csswg.org/css-mixins-1/#mixin-body).</summary>
    internal bool IsInMixinBody()
    {
        for (var rule = this; rule is not null; rule = rule.ParentRule)
        {
            if (rule is CssMixinResultRule)
                return false;

            if (rule is CssMixinRule)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the rule is a @result rule of a mixin or is in one.</summary>
    internal bool IsInMixinResult()
    {
        for (var rule = this; rule is not null; rule = rule.ParentRule)
        {
            if (rule is CssMixinResultRule)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the rule is a @function rule or is in the body of one (https://drafts.csswg.org/css-mixins-1/#function-body).</summary>
    internal bool IsInFunctionRule()
    {
        for (var rule = this; rule is not null; rule = rule.ParentRule)
        {
            if (rule is CssFunctionRule)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the style rules of the rule are scoped style rules: the closest enclosing style or @scope rule is a @scope rule.</summary>
    internal bool IsInScopeRule()
    {
        for (var rule = this; rule is not null; rule = rule.ParentRule)
        {
            if (rule is CssStyleRule)
                return false;

            if (rule is CssScopeRule)
                return true;
        }

        return false;
    }
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssrulelist-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssrulelist"/>
[IdlInterface("CSSRuleList", "https://drafts.csswg.org/cssom/#cssrulelist")]
public sealed class CssRuleList : IReadOnlyList<CssRule>
{
    private readonly List<CssRule> _rules;

    internal CssRuleList(List<CssRule> rules)
    {
        _rules = rules;
    }

    /// <summary>Returns the rule at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrulelist-item"/>
    [IdlMember("item", "https://drafts.csswg.org/cssom/#dom-cssrulelist-item")]
    public CssRule? Item(uint index) => index < (uint)_rules.Count ? _rules[(int)index] : null;

    /// <summary>Gets the rule at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrulelist-item"/>
    [IdlMember("getter", "https://drafts.csswg.org/cssom/#dom-cssrulelist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssRule? this[uint index] => Item(index);

    /// <summary>Gets the rule at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssRule this[int index] => _rules[index];

    /// <summary>Gets the number of rules in the list.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssrulelist-length"/>
    [IdlMember("length", "https://drafts.csswg.org/cssom/#dom-cssrulelist-length")]
    public uint Length => (uint)_rules.Count;

    /// <summary>Gets the number of rules in the list.</summary>
    [NotInIdl]
    public int Count => _rules.Count;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<CssRule> GetEnumerator() => _rules.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssgroupingrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssgroupingrule"/>
[IdlInterface("CSSGroupingRule", "https://drafts.csswg.org/cssom/#cssgroupingrule")]
public abstract class CssGroupingRule : CssRule
{
    private readonly List<CssRule> _rules = [];
    private CssRuleList? _ruleList;

    private protected CssGroupingRule()
    {
    }

    internal override List<CssRule> ChildRules => _rules;

    internal override bool HasMultilineBlock => true;

    /// <summary>Gets the list of the child rules of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssgroupingrule-cssrules"/>
    [IdlMember("cssRules", "https://drafts.csswg.org/cssom/#dom-cssgroupingrule-cssrules")]
    public CssRuleList CssRules => _ruleList ??= new CssRuleList(_rules);

    /// <summary>Adds a child rule parsed with the rule.</summary>
    internal void AddChildRule(CssRule rule)
    {
        rule.ParentRule = this;
        _rules.Add(rule);
    }

    /// <summary>
    /// Parses a rule and inserts it in the child rules at the given index, and returns the index at which it was
    /// inserted.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssgroupingrule-insertrule"/>
    [IdlMember("insertRule", "https://drafts.csswg.org/cssom/#dom-cssgroupingrule-insertrule")]
    public uint InsertRule(string rule, uint index = 0)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var parser = new CssRuleParser(ParentStyleSheet) { InScopeRule = IsInScopeRule(), InMixinResult = IsInMixinResult() };
        var nested = IsNestedInStyleRule();
        var context = nested ? CssRuleContext.Nested : this is CssPageRule ? CssRuleContext.Page : IsInFunctionRule() ? CssRuleContext.Function : IsInMixinBody() ? CssRuleContext.Mixin : CssRuleContext.Grouping;
        return parser.InsertRule(_rules, rule, index, context, parentRule: this, ParentStyleSheet);
    }

    /// <summary>Removes the child rule at the given index.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssgroupingrule-deleterule"/>
    [IdlMember("deleteRule", "https://drafts.csswg.org/cssom/#dom-cssgroupingrule-deleterule")]
    public void DeleteRule(uint index) => CssRuleParser.RemoveRule(_rules, index, ParentStyleSheet);
}

/// <summary>https://drafts.csswg.org/css-conditional-3/#the-cssconditionrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-conditional-3/#cssconditionrule"/>
[IdlInterface("CSSConditionRule", "https://drafts.csswg.org/css-conditional-3/#cssconditionrule")]
public abstract class CssConditionRule : CssGroupingRule
{
    private protected CssConditionRule()
    {
    }

    /// <summary>Gets the text of the condition of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-3/#dom-cssconditionrule-conditiontext"/>
    [IdlMember("conditionText", "https://drafts.csswg.org/css-conditional-3/#dom-cssconditionrule-conditiontext")]
    public abstract string ConditionText { get; }
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssstylerule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssstylerule"/>
[IdlInterface("CSSStyleRule", "https://drafts.csswg.org/cssom/#cssstylerule")]
public sealed class CssStyleRule : CssGroupingRule
{
    private CssStyleProperties? _style;
    private TypedOm.StylePropertyMap? _styleMap;

    internal CssStyleRule(SelectorList selectors, List<CssDeclarationEntry> declarations)
    {
        Selectors = selectors;
        _style = new CssStyleProperties(this, declarations);
    }

    /// <summary>The parsed selector list of the rule (the parsed form of <see cref="SelectorText"/>).</summary>
    [NotInIdl("Parsed selector list")]
    public SelectorList Selectors { get; private set; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.StyleRule"/> for style rules.</summary>
    public override ushort Type => StyleRule;

    /// <summary>Gets or sets the serialized selector list of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylerule-selectortext"/>
    [IdlMember("selectorText", "https://drafts.csswg.org/cssom/#dom-cssstylerule-selectortext")]
    public string SelectorText
    {
        get => SelectorSerializer.Serialize(Selectors);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var parser = new CssRuleParser(ParentStyleSheet);
            var values = CssParser.ParseListOfComponentValues(value, out var source);
            if (parser.ParseStyleRuleSelector(values, nested: ParentRule?.IsNestedInStyleRule() ?? false, scoped: ParentRule?.IsInScopeRule() ?? false, source) is { } selectors)
            {
                Selectors = selectors;
                Cascade.CssStyleVersion.Increment(this);
            }
        }
    }

    /// <summary>Gets the declaration block of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylerule-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-cssstylerule-style")]
    public CssStyleProperties Style => _style ??= new CssStyleProperties(this);

    /// <summary>The declarations of the rule as <see cref="TypedOm.CssStyleValue"/> objects (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylerule-stylemap).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylerule-stylemap"/>
    [IdlMember("styleMap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssstylerule-stylemap")]
    public TypedOm.StylePropertyMap StyleMap => _styleMap ??= new TypedOm.StylePropertyMap(Style);

    internal override string GetPrelude() => SelectorText;

    /// <summary>The declarations are written on the line of the selectors, unless the rule contains nested rules.</summary>
    internal override bool HasMultilineBlock => ChildRules.Count > 0;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-nesting/#the-cssnestrule</summary>
/// <seealso href="https://drafts.csswg.org/css-nesting-1/#cssnesteddeclarations"/>
[IdlInterface("CSSNestedDeclarations", "https://drafts.csswg.org/css-nesting-1/#cssnesteddeclarations")]
public sealed class CssNestedDeclarations : CssRule
{
    internal CssNestedDeclarations(List<CssDeclarationEntry> declarations)
    {
        Style = new CssStyleProperties(this, declarations);
    }

    /// <summary>Gets the declarations of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-nesting-1/#dom-cssnesteddeclarations-style"/>
    [IdlMember("style", "https://drafts.csswg.org/css-nesting-1/#dom-cssnesteddeclarations-style")]
    public CssStyleProperties Style { get; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.StyleRule"/> for nested declarations.</summary>
    public override ushort Type => StyleRule;

    /// <summary>The nested declarations have no prelude and no block: only their declarations are written.</summary>
    internal override string GetPrelude() => "";

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-conditional-3/#the-cssmediarule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-conditional-3/#cssmediarule"/>
[IdlInterface("CSSMediaRule", "https://drafts.csswg.org/css-conditional-3/#cssmediarule")]
public sealed class CssMediaRule : CssConditionRule
{
    internal CssMediaRule(MediaList media)
    {
        Media = media;
        media.SetOwner(this);
    }

    /// <summary>Gets the media query list of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-3/#dom-cssmediarule-media"/>
    [IdlMember("media", "https://drafts.csswg.org/css-conditional-3/#dom-cssmediarule-media")]
    public MediaList Media { get; }

    /// <summary>https://drafts.csswg.org/css-conditional-5/#dom-cssmediarule-matches: there is no viewport, so the media features are not evaluated.</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-cssmediarule-matches"/>
    [IdlMember("matches", "https://drafts.csswg.org/css-conditional-5/#dom-cssmediarule-matches")]
    public bool Matches => MediaQueryEvaluator.Matches(Media, GetMediaEnvironment());

    /// <summary>Gets the serialized media query list of the rule.</summary>
    public override string ConditionText => Media.MediaText;

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.MediaRule"/> for <c>@media</c> rules.</summary>
    public override ushort Type => MediaRule;

    internal override string GetPrelude() => "@media " + Media.MediaText;
}

/// <summary>
/// https://drafts.csswg.org/mediaqueries-5/#csscustommediarule: a custom media query (<c>@custom-media --name query;</c>).
/// The rule is exposed in the CSSOM, but the references to custom media queries (<c>(--name)</c>) are not resolved: they are
/// &lt;general-enclosed&gt; and evaluate to unknown.
/// </summary>
/// <seealso href="https://drafts.csswg.org/mediaqueries-5/#csscustommediarule"/>
[IdlInterface("CSSCustomMediaRule", "https://drafts.csswg.org/mediaqueries-5/#csscustommediarule")]
public sealed class CssCustomMediaRule : CssRule
{
    internal CssCustomMediaRule(string name, MediaListOrBoolean query)
    {
        Name = name;
        Query = query;
    }

    /// <summary>Gets the name of the custom media query, including the leading <c>--</c>.</summary>
    /// <seealso href="https://drafts.csswg.org/mediaqueries-5/#dom-csscustommediarule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/mediaqueries-5/#dom-csscustommediarule-name")]
    public string Name { get; }

    /// <summary>The media query list of the rule, or the boolean of the <c>true</c> and <c>false</c> queries.</summary>
    /// <seealso href="https://drafts.csswg.org/mediaqueries-5/#dom-csscustommediarule-query"/>
    [IdlMember("query", "https://drafts.csswg.org/mediaqueries-5/#dom-csscustommediarule-query")]
    public MediaListOrBoolean Query { get; }

    internal override bool IsStatement => true;

    internal override string GetPrelude()
    {
        var query = Query.Value switch
        {
            bool value => value ? "true" : "false",
            MediaList list => list.MediaText,
            _ => "",
        };

        return "@custom-media " + CssSerializer.SerializeIdentifier(Name) + (query.Length > 0 ? " " + query : "");
    }
}

/// <summary>The (MediaList or boolean) union (https://drafts.csswg.org/mediaqueries-5/#typedefdef-custommediaquery).</summary>
public readonly struct MediaListOrBoolean : IUnionValue, IEquatable<MediaListOrBoolean>
{
    private MediaListOrBoolean(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="Om.MediaList"/> or a <see cref="bool"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the media query list when the value is a media query list, otherwise null.</summary>
    [NotInIdl]
    public MediaList? MediaList => Value as MediaList;

    /// <summary>Gets the boolean when the value is a boolean, otherwise null.</summary>
    [NotInIdl]
    public bool? BooleanValue => Value as bool?;

    /// <summary>Creates a union value from a media query list.</summary>
    [NotInIdl]
    public static implicit operator MediaListOrBoolean(MediaList value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value from a boolean.</summary>
    [NotInIdl]
    public static implicit operator MediaListOrBoolean(bool value) => new(value);

    /// <summary>Creates a union value from a media query list.</summary>
    [NotInIdl]
    public static MediaListOrBoolean FromMediaList(MediaList value) => value;

    /// <summary>Creates a union value from a boolean.</summary>
    [NotInIdl]
    public static MediaListOrBoolean FromBoolean(bool value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(MediaListOrBoolean other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is MediaListOrBoolean other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two values hold the same media query list or the same boolean.</summary>
    [NotInIdl]
    public static bool operator ==(MediaListOrBoolean left, MediaListOrBoolean right) => left.Equals(right);

    /// <summary>Returns whether the two values hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(MediaListOrBoolean left, MediaListOrBoolean right) => !left.Equals(right);
}

/// <summary>https://drafts.csswg.org/css-conditional-3/#the-csssupportsrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-conditional-3/#csssupportsrule"/>
[IdlInterface("CSSSupportsRule", "https://drafts.csswg.org/css-conditional-3/#csssupportsrule")]
public sealed class CssSupportsRule : CssConditionRule
{
    private readonly bool _matches;

    internal CssSupportsRule(string conditionText, bool matches)
    {
        ConditionText = conditionText;
        _matches = matches;
    }

    /// <summary>Gets the text of the supports condition of the rule.</summary>
    public override string ConditionText { get; }

    /// <summary>
    /// Gets a value indicating whether the supports condition of the rule currently evaluates to true.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csssupportsrule-matches"/>
    [IdlMember("matches", "https://drafts.csswg.org/css-conditional-5/#dom-csssupportsrule-matches")]
    public bool Matches => _matches;

    /// <summary>
    /// Gets the legacy numeric type of the rule: <see cref="CssRule.SupportsRule"/> for <c>@supports</c> rules.
    /// </summary>
    public override ushort Type => SupportsRule;

    internal override string GetPrelude() => "@supports " + ConditionText;
}

/// <summary>https://drafts.csswg.org/css-conditional-5/#dictdef-csscontainercondition</summary>
/// <seealso href="https://drafts.csswg.org/css-conditional-5/#dictdef-csscontainercondition"/>
[IdlInterface("CSSContainerCondition", "https://drafts.csswg.org/css-conditional-5/#dictdef-csscontainercondition")]
public sealed class CssContainerCondition
{
    /// <summary>
    /// Gets the name of the container targeted by the condition, or an empty string when the condition has no name.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-name")]
    public required string Name { get; init; }

    /// <summary>Gets the text of the container query of the condition.</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-query"/>
    [IdlMember("query", "https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-query")]
    public required string Query { get; init; }
}

/// <summary>https://drafts.csswg.org/css-conditional-5/#the-csscontainerrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-conditional-5/#csscontainerrule"/>
[IdlInterface("CSSContainerRule", "https://drafts.csswg.org/css-conditional-5/#csscontainerrule")]
public sealed class CssContainerRule : CssConditionRule
{
    internal CssContainerRule(List<ContainerCondition> conditions)
    {
        ParsedConditions = conditions;
        Conditions = [.. conditions.Select(c => new CssContainerCondition { Name = c.NameText, Query = c.QueryText })];
    }

    /// <summary>Gets the container conditions of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-conditions"/>
    [IdlMember("conditions", "https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-conditions")]
    public IReadOnlyList<CssContainerCondition> Conditions { get; }

    /// <summary>The parsed &lt;container-condition&gt;s, evaluated by the cascade.</summary>
    internal IReadOnlyList<ContainerCondition> ParsedConditions { get; }

    /// <summary>
    /// Gets the name of the container targeted by the rule, or an empty string when the rule has not exactly one
    /// condition.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-containername"/>
    [IdlMember("containerName", "https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-containername")]
    public string ContainerName => Conditions.Count == 1 ? Conditions[0].Name : "";

    /// <summary>
    /// Gets the container query of the rule, or an empty string when the rule has not exactly one condition.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-containerquery"/>
    [IdlMember("containerQuery", "https://drafts.csswg.org/css-conditional-5/#dom-csscontainerrule-containerquery")]
    public string ContainerQuery => Conditions.Count == 1 ? Conditions[0].Query : "";

    /// <inheritdoc/>
    public override string ConditionText => string.Join(", ", Conditions.Select(c => c.Name.Length == 0 ? c.Query : c.Query.Length == 0 ? c.Name : c.Name + " " + c.Query));

    internal override string GetPrelude() => "@container " + ConditionText;
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssimportrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssimportrule"/>
[IdlInterface("CSSImportRule", "https://drafts.csswg.org/cssom/#cssimportrule")]
public sealed class CssImportRule : CssRule
{
    internal CssImportRule(string href, MediaList media, string? layerName, string? supportsText, bool supportsMatches)
    {
        Href = href;
        Media = media;
        media.SetOwner(this);
        LayerName = layerName;
        SupportsText = supportsText;
        SupportsMatches = supportsMatches;
    }

    /// <summary>Gets the URL of the imported style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssimportrule-href"/>
    [IdlMember("href", "https://drafts.csswg.org/cssom/#dom-cssimportrule-href")]
    public string Href { get; }

    /// <summary>Gets the media query list that conditions the import.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssimportrule-media"/>
    [IdlMember("media", "https://drafts.csswg.org/cssom/#dom-cssimportrule-media")]
    public MediaList Media { get; }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#dom-cssimportrule-stylesheet: the imported style sheet, or null until it is fetched
    /// with the <see cref="DocumentLoader"/> of the browsing context (it is never fetched without a loader, for a constructed
    /// style sheet, or when the <c>supports()</c> condition is false).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssimportrule-stylesheet"/>
    [IdlMember("styleSheet", "https://drafts.csswg.org/cssom/#dom-cssimportrule-stylesheet")]
    public CssStyleSheet? StyleSheet { get; internal set; }

    /// <summary>
    /// Gets the name of the cascade layer in which the imported style sheet is placed, an empty string for an anonymous
    /// layer, or null when the import has no <c>layer</c>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssimportrule-layername"/>
    [IdlMember("layerName", "https://drafts.csswg.org/cssom/#dom-cssimportrule-layername")]
    public string? LayerName { get; }

    /// <summary>
    /// Gets the text of the <c>supports()</c> condition of the import, or null when the import has no condition.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssimportrule-supportstext"/>
    [IdlMember("supportsText", "https://drafts.csswg.org/cssom/#dom-cssimportrule-supportstext")]
    public string? SupportsText { get; }

    /// <summary>Whether the <c>supports()</c> condition is true (or there is no condition).</summary>
    internal bool SupportsMatches { get; }

    /// <summary>The request URL modifiers of the url(), in their canonical order (https://drafts.csswg.org/css-values-5/#request-url-modifiers), or null.</summary>
    internal string? UrlModifiers { get; init; }

    /// <summary>The parsed request URL modifiers of the url(), applied to the request of the style sheet.</summary>
    internal CssRequestModifiers RequestModifiers => UrlModifiers is { } modifiers ? CssUrlModifiers.Parse(modifiers) ?? CssRequestModifiers.None : CssRequestModifiers.None;

    /// <summary>Whether the rules of the imported style sheet are scoped by <c>scope()</c> (https://drafts.csswg.org/css-cascade-6/#at-import).</summary>
    internal bool Scoped { get; init; }

    /// <summary>The &lt;scope-start&gt; of <c>scope()</c>, or null (the scoping root is the parent of the owner node).</summary>
    internal string? ScopeStart { get; init; }

    /// <summary>The &lt;scope-end&gt; of <c>scope()</c>, or null.</summary>
    internal string? ScopeEnd { get; init; }

    internal SelectorList? ScopeStartSelectors { get; init; }

    internal SelectorList? ScopeEndSelectors { get; init; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.ImportRule"/> for <c>@import</c> rules.</summary>
    public override ushort Type => ImportRule;

    internal override bool IsStatement => true;

    internal override string GetPrelude()
    {
        var builder = new StringBuilder("@import ").Append(UrlModifiers is { } modifiers ? CssUrlModifiers.Serialize("url", Href, modifiers) : CssSerializer.SerializeUrl(Href));
        if (LayerName is { } layer)
        {
            builder.Append(layer.Length == 0 ? " layer" : " layer(" + layer + ")");
        }

        if (Scoped)
        {
            builder.Append(" scope(");
            if (ScopeStart is not null)
            {
                builder.Append('(').Append(ScopeStart).Append(')');
            }

            if (ScopeEnd is not null)
            {
                builder.Append(ScopeStart is null ? "to (" : " to (").Append(ScopeEnd).Append(')');
            }

            builder.Append(')');
        }

        if (SupportsText is { } supports)
        {
            builder.Append(" supports(").Append(supports).Append(')');
        }

        var media = Media.MediaText;
        if (media.Length > 0)
        {
            builder.Append(' ').Append(media);
        }

        return builder.ToString();
    }
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssnamespacerule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssnamespacerule"/>
[IdlInterface("CSSNamespaceRule", "https://drafts.csswg.org/cssom/#cssnamespacerule")]
public sealed class CssNamespaceRule : CssRule
{
    internal CssNamespaceRule(string prefix, string namespaceUri)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
    }

    /// <summary>Gets the namespace URI of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssnamespacerule-namespaceuri"/>
    [IdlMember("namespaceURI", "https://drafts.csswg.org/cssom/#dom-cssnamespacerule-namespaceuri")]
    public string NamespaceUri { get; }

    /// <summary>Gets the namespace prefix of the rule, or an empty string for the default namespace.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssnamespacerule-prefix"/>
    [IdlMember("prefix", "https://drafts.csswg.org/cssom/#dom-cssnamespacerule-prefix")]
    public string Prefix { get; }

    /// <summary>
    /// Gets the legacy numeric type of the rule: <see cref="CssRule.NamespaceRule"/> for <c>@namespace</c> rules.
    /// </summary>
    public override ushort Type => NamespaceRule;

    internal override bool IsStatement => true;

    internal override string GetPrelude()
    {
        var builder = new StringBuilder("@namespace ");
        if (Prefix.Length > 0)
        {
            builder.Append(CssSerializer.SerializeIdentifier(Prefix)).Append(' ');
        }

        return builder.Append(CssSerializer.SerializeUrl(NamespaceUri)).ToString();
    }
}

/// <summary>https://drafts.csswg.org/css-fonts/#cssfontfacerule</summary>
/// <seealso href="https://drafts.csswg.org/css-fonts-5/#cssfontfacerule"/>
[IdlInterface("CSSFontFaceRule", "https://drafts.csswg.org/css-fonts-5/#cssfontfacerule")]
public sealed class CssFontFaceRule : CssRule
{
    internal CssFontFaceRule(List<CssDeclarationEntry> declarations)
    {
        Style = new CssFontFaceDescriptors(this, declarations);
    }

    /// <summary>Gets the descriptors of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacerule-style"/>
    [IdlMember("style", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacerule-style")]
    public CssFontFaceDescriptors Style { get; }

    /// <summary>
    /// Gets the legacy numeric type of the rule: <see cref="CssRule.FontFaceRule"/> for <c>@font-face</c> rules.
    /// </summary>
    public override ushort Type => FontFaceRule;

    internal override string GetPrelude() => "@font-face";

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/cssom/#the-csspagerule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#csspagerule"/>
[IdlInterface("CSSPageRule", "https://drafts.csswg.org/cssom/#csspagerule")]
public sealed class CssPageRule : CssGroupingRule
{
    internal CssPageRule(string selectorText, List<CssDeclarationEntry> declarations)
    {
        PageSelector = selectorText;
        Style = new CssPageDescriptors(this, declarations);
    }

    private string PageSelector { get; set; }

    /// <summary>Gets or sets the serialized page selector of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagerule-selectortext"/>
    [IdlMember("selectorText", "https://drafts.csswg.org/cssom/#dom-csspagerule-selectortext")]
    public string SelectorText
    {
        get => PageSelector;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (CssRuleParser.ParsePageSelector(CssParser.ParseListOfComponentValues(value)) is { } selector)
            {
                PageSelector = selector;
            }
        }
    }

    /// <summary>Gets the descriptors of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagerule-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-csspagerule-style")]
    public CssPageDescriptors Style { get; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.PageRule"/> for <c>@page</c> rules.</summary>
    public override ushort Type => PageRule;

    internal override string GetPrelude() => PageSelector.Length > 0 ? "@page " + PageSelector : "@page";

    /// <summary>The margin rules are written on the line of the selector, after the declarations.</summary>
    internal override bool HasMultilineBlock => false;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssmarginrule-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssmarginrule"/>
[IdlInterface("CSSMarginRule", "https://drafts.csswg.org/cssom/#cssmarginrule")]
public sealed class CssMarginRule : CssRule
{
    internal CssMarginRule(string name, List<CssDeclarationEntry> declarations)
    {
        Name = name;
        Style = new CssStyleProperties(this, declarations, "@" + name);
    }

    /// <summary>Gets the name of the page margin box of the rule (e.g. <c>top-left</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssmarginrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/cssom/#dom-cssmarginrule-name")]
    public string Name { get; }

    /// <summary>The descriptors of the margin rule: a CSSStyleProperties object, as in browsers.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssmarginrule-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-cssmarginrule-style")]
    public CssStyleDeclaration Style { get; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.MarginRule"/> for page margin rules.</summary>
    public override ushort Type => MarginRule;

    internal override string GetPrelude() => "@" + Name;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-animations/#interface-csskeyframesrule</summary>
/// <seealso href="https://drafts.csswg.org/css-animations-1/#csskeyframesrule"/>
[IdlInterface("CSSKeyframesRule", "https://drafts.csswg.org/css-animations-1/#csskeyframesrule")]
public sealed class CssKeyframesRule : CssRule
{
    private readonly List<CssRule> _rules = [];
    private CssRuleList? _ruleList;
    private string _name;

    internal CssKeyframesRule(string name)
    {
        _name = name;
    }

    internal override List<CssRule> ChildRules => _rules;

    /// <summary>Gets or sets the name of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-name")]
    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _name = value;
            OnKeyframesChanged();
        }
    }

    /// <summary>Gets the list of the keyframe rules.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-cssrules"/>
    [IdlMember("cssRules", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-cssrules")]
    public CssRuleList CssRules => _ruleList ??= new CssRuleList(_rules);

    /// <summary>Gets the number of keyframe rules.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-length"/>
    [IdlMember("length", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-length")]
    public uint Length => (uint)_rules.Count;

    /// <summary>Gets the keyframe rule at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-getter"/>
    [IdlMember("getter", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-getter")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssKeyframeRule? this[uint index] => index < (uint)_rules.Count ? (CssKeyframeRule)_rules[(int)index] : null;

    /// <summary>
    /// Gets the legacy numeric type of the rule: <see cref="CssRule.KeyframesRule"/> for <c>@keyframes</c> rules.
    /// </summary>
    public override ushort Type => KeyframesRule;

    internal void AddKeyframe(CssKeyframeRule rule)
    {
        rule.ParentRule = this;
        _rules.Add(rule);
    }

    /// <summary>Parses a keyframe rule and appends it to the keyframes.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-appendrule"/>
    [IdlMember("appendRule", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-appendrule")]
    public void AppendRule(string rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (new CssRuleParser(ParentStyleSheet).ParseKeyframeText(rule) is { } keyframe)
        {
            AddKeyframe(keyframe);
            keyframe.SetParentStyleSheet(ParentStyleSheet);
            OnKeyframesChanged();
        }
    }

    /// <summary>The animations that use the keyframes are updated (https://drafts.csswg.org/css-animations-1/#keyframes).</summary>
    internal void OnKeyframesChanged()
    {
        Cascade.CssStyleVersion.Increment(this);
        ParentStyleSheet?.RequestCssResourcesUpdate();
    }

    /// <summary>Removes the last keyframe rule whose key text matches the given selector.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-deleterule"/>
    [IdlMember("deleteRule", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-deleterule")]
    public void DeleteRule(string select)
    {
        ArgumentNullException.ThrowIfNull(select);
        var index = FindIndex(select);
        if (index < 0)
            return;

        var rule = _rules[index];
        _rules.RemoveAt(index);
        rule.ParentRule = null;
        rule.SetParentStyleSheet(null);
        OnKeyframesChanged();
    }

    /// <summary>
    /// Returns the last keyframe rule whose key text matches the given selector, or null when there is none.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-findrule"/>
    [IdlMember("findRule", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframesrule-findrule")]
    public CssKeyframeRule? FindRule(string select)
    {
        ArgumentNullException.ThrowIfNull(select);
        var index = FindIndex(select);
        return index < 0 ? null : (CssKeyframeRule)_rules[index];
    }

    /// <summary>Returns the index of the last keyframe whose key list is the selector.</summary>
    private int FindIndex(string select)
    {
        var keys = CssRuleParser.ParseKeyframeSelector(CssParser.ParseListOfComponentValues(select));
        if (keys is null)
            return -1;

        for (var i = _rules.Count - 1; i >= 0; i--)
        {
            if (((CssKeyframeRule)_rules[i]).Keys.SequenceEqual(keys))
                return i;
        }

        return -1;
    }

    internal override string GetPrelude() => "@keyframes " + (IsValidIdentifierName(_name) ? CssSerializer.SerializeIdentifier(_name) : CssSerializer.SerializeString(_name));

    internal override bool HasMultilineBlock => true;

    /// <summary>A keyframes name is serialized as an identifier unless it is a CSS-wide keyword or "none".</summary>
    private static bool IsValidIdentifierName(string name)
    {
        var lower = InfraAscii.ToAsciiLowercase(name);
        return name.Length > 0 && !ValueMatcher.CssWideKeywords.Contains(lower) && lower is not "none" and not "default";
    }
}

/// <summary>https://drafts.csswg.org/css-animations/#interface-csskeyframerule</summary>
/// <seealso href="https://drafts.csswg.org/css-animations-1/#csskeyframerule"/>
[IdlInterface("CSSKeyframeRule", "https://drafts.csswg.org/css-animations-1/#csskeyframerule")]
public sealed class CssKeyframeRule : CssRule
{
    internal CssKeyframeRule(List<string> keys, List<CssDeclarationEntry> declarations)
    {
        Keys = keys;
        Style = new CssStyleProperties(this, declarations, CssStyleDeclaration.KeyframeContext);
    }

    /// <summary>The serialized keyframe selectors (e.g. 0%, entry 10%).</summary>
    internal List<string> Keys { get; private set; }

    /// <summary>Gets or sets the serialized keyframe selector list of the rule (e.g. <c>0%, 50%</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframerule-keytext"/>
    [IdlMember("keyText", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframerule-keytext")]
    public string KeyText
    {
        get => string.Join(", ", Keys);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Keys = CssRuleParser.ParseKeyframeSelector(CssParser.ParseListOfComponentValues(value)) ?? throw DomException.Syntax("The key text is invalid");
            (ParentRule as CssKeyframesRule)?.OnKeyframesChanged();
        }
    }

    /// <summary>Gets the declaration block of the keyframe.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-csskeyframerule-style"/>
    [IdlMember("style", "https://drafts.csswg.org/css-animations-1/#dom-csskeyframerule-style")]
    public CssStyleProperties Style { get; }

    /// <summary>Gets the legacy numeric type of the rule: <see cref="CssRule.KeyframeRule"/> for keyframe rules.</summary>
    public override ushort Type => KeyframeRule;

    internal override string GetPrelude() => KeyText;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(Style.Declarations);
}

/// <summary>https://drafts.csswg.org/css-cascade-5/#csslayerblockrule</summary>
/// <seealso href="https://drafts.csswg.org/css-cascade-5/#csslayerblockrule"/>
[IdlInterface("CSSLayerBlockRule", "https://drafts.csswg.org/css-cascade-5/#csslayerblockrule")]
public sealed class CssLayerBlockRule : CssGroupingRule
{
    internal CssLayerBlockRule(string name)
    {
        Name = name;
    }

    /// <summary>Gets the name of the cascade layer, or an empty string for an anonymous layer.</summary>
    /// <seealso href="https://drafts.csswg.org/css-cascade-5/#dom-csslayerblockrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-cascade-5/#dom-csslayerblockrule-name")]
    public string Name { get; }

    internal override string GetPrelude() => Name.Length > 0 ? "@layer " + Name : "@layer";
}

/// <summary>https://drafts.csswg.org/css-cascade-5/#csslayerstatementrule</summary>
/// <seealso href="https://drafts.csswg.org/css-cascade-5/#csslayerstatementrule"/>
[IdlInterface("CSSLayerStatementRule", "https://drafts.csswg.org/css-cascade-5/#csslayerstatementrule")]
public sealed class CssLayerStatementRule : CssRule
{
    internal CssLayerStatementRule(List<string> nameList)
    {
        NameList = nameList;
    }

    /// <summary>Gets the names of the cascade layers declared by the statement.</summary>
    /// <seealso href="https://drafts.csswg.org/css-cascade-5/#dom-csslayerstatementrule-namelist"/>
    [IdlMember("nameList", "https://drafts.csswg.org/css-cascade-5/#dom-csslayerstatementrule-namelist")]
    public IReadOnlyList<string> NameList { get; }

    internal override bool IsStatement => true;

    internal override string GetPrelude() => "@layer " + string.Join(", ", NameList);
}

/// <summary>https://drafts.csswg.org/css-cascade-6/#cssscoperule</summary>
/// <seealso href="https://drafts.csswg.org/css-cascade-6/#cssscoperule"/>
[IdlInterface("CSSScopeRule", "https://drafts.csswg.org/css-cascade-6/#cssscoperule")]
public sealed class CssScopeRule : CssGroupingRule
{
    internal CssScopeRule(string? start, string? end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// Gets the text of the scoping root selector of the rule, or null when the rule has no scoping root.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-cascade-6/#dom-cssscoperule-start"/>
    [IdlMember("start", "https://drafts.csswg.org/css-cascade-6/#dom-cssscoperule-start")]
    public string? Start { get; }

    /// <summary>
    /// Gets the text of the scoping limit selector of the rule, or null when the rule has no scoping limit.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-cascade-6/#dom-cssscoperule-end"/>
    [IdlMember("end", "https://drafts.csswg.org/css-cascade-6/#dom-cssscoperule-end")]
    public string? End { get; }

    /// <summary>The parsed &lt;scope-start&gt; selector (the parsed form of <see cref="Start"/>).</summary>
    [NotInIdl("Parsed selector list")]
    public SelectorList? StartSelectors { get; internal init; }

    /// <summary>The parsed &lt;scope-end&gt; selector (the parsed form of <see cref="End"/>; a relative selector: the scoping root is the nesting selector).</summary>
    [NotInIdl("Parsed selector list")]
    public SelectorList? EndSelectors { get; internal init; }

    internal override string GetPrelude()
    {
        var builder = new StringBuilder("@scope");
        if (Start is not null)
        {
            builder.Append(" (").Append(Start).Append(')');
        }

        if (End is not null)
        {
            builder.Append(" to (").Append(End).Append(')');
        }

        return builder.ToString();
    }
}

/// <summary>https://drafts.csswg.org/css-transitions-2/#the-cssstartingstylerule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-transitions-2/#cssstartingstylerule"/>
[IdlInterface("CSSStartingStyleRule", "https://drafts.csswg.org/css-transitions-2/#cssstartingstylerule")]
public sealed class CssStartingStyleRule : CssGroupingRule
{
    internal CssStartingStyleRule()
    {
    }

    internal override string GetPrelude() => "@starting-style";
}

/// <summary>https://drafts.css-houdini.org/css-properties-values-api/#the-css-property-rule-interface</summary>
/// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#csspropertyrule"/>
[IdlInterface("CSSPropertyRule", "https://drafts.css-houdini.org/css-properties-values-api-1/#csspropertyrule")]
public sealed class CssPropertyRule : CssRule
{
    internal CssPropertyRule(string name, string syntax, Cascade.CustomPropertySyntax syntaxDefinition, bool inherits, string? initialValue)
    {
        Name = name;
        Syntax = syntax;
        Inherits = inherits;
        InitialValue = initialValue;
        Registration = new Cascade.CustomPropertyRegistration(name, syntaxDefinition, inherits, initialValue);
    }

    /// <summary>The registration represented by the rule.</summary>
    internal Cascade.CustomPropertyRegistration Registration { get; }

    /// <summary>Gets the name of the registered custom property, including the leading <c>--</c>.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-name"/>
    [IdlMember("name", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-name")]
    public string Name { get; }

    /// <summary>Gets the syntax string of the registered custom property.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-syntax"/>
    [IdlMember("syntax", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-syntax")]
    public string Syntax { get; }

    /// <summary>Gets a value indicating whether the registered custom property inherits.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-inherits"/>
    [IdlMember("inherits", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-inherits")]
    public bool Inherits { get; }

    /// <summary>Gets the initial value of the registered custom property, or null when the rule has none.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-initialvalue"/>
    [IdlMember("initialValue", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-csspropertyrule-initialvalue")]
    public string? InitialValue { get; }

    internal override string GetPrelude() => "@property " + CssSerializer.SerializeIdentifier(Name);

    internal override List<CssSerializedDeclaration> GetDeclarations()
    {
        List<CssSerializedDeclaration> descriptors =
        [
            new("syntax", CssSerializer.SerializeString(Syntax), Important: false),
            new("inherits", Inherits ? "true" : "false", Important: false),
        ];

        if (InitialValue is not null)
        {
            descriptors.Add(new("initial-value", InitialValue, Important: false));
        }

        return descriptors;
    }
}

/// <summary>
/// https://drafts.csswg.org/css-view-transitions-2/#cssviewtransitionrule: the <c>@view-transition</c> rule, which opts the
/// document in to the cross-document view transitions (https://drafts.csswg.org/css-view-transitions-2/#view-transition-rule).
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#cssviewtransitionrule"/>
[IdlInterface("CSSViewTransitionRule", "https://drafts.csswg.org/css-view-transitions-2/#cssviewtransitionrule")]
public sealed class CssViewTransitionRule : CssRule
{
    internal CssViewTransitionRule(string navigation, IReadOnlyList<string> types, bool hasTypes)
    {
        Navigation = navigation;
        Types = types;
        HasTypes = hasTypes;
    }

    /// <summary>Gets the value of the <c>navigation</c> descriptor (<c>auto</c> or <c>none</c>), or an empty string when it is missing or invalid.</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-cssviewtransitionrule-navigation"/>
    [IdlMember("navigation", "https://drafts.csswg.org/css-view-transitions-2/#dom-cssviewtransitionrule-navigation")]
    public string Navigation { get; }

    /// <summary>Gets the types of the <c>types</c> descriptor (none for <c>none</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#dom-cssviewtransitionrule-types"/>
    [IdlMember("types", "https://drafts.csswg.org/css-view-transitions-2/#dom-cssviewtransitionrule-types")]
    public IReadOnlyList<string> Types { get; }

    /// <summary>Whether the rule has a valid <c>types</c> descriptor.</summary>
    internal bool HasTypes { get; }

    internal override string GetPrelude() => "@view-transition";

    internal override List<CssSerializedDeclaration> GetDeclarations()
    {
        List<CssSerializedDeclaration> descriptors = [];
        if (Navigation.Length > 0)
        {
            descriptors.Add(new("navigation", Navigation, Important: false));
        }

        if (HasTypes)
        {
            descriptors.Add(new("types", Types.Count == 0 ? "none" : string.Join(' ', Types.Select(CssSerializer.SerializeIdentifier)), Important: false));
        }

        return descriptors;
    }
}

/// <summary>https://drafts.csswg.org/css-counter-styles/#the-csscounterstylerule-interface</summary>
/// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#csscounterstylerule"/>
[IdlInterface("CSSCounterStyleRule", "https://drafts.csswg.org/css-counter-styles-3/#csscounterstylerule")]
public sealed class CssCounterStyleRule : CssRule
{
    private readonly Dictionary<string, string> _descriptors;

    /// <summary>The component values of the descriptors, as written (the symbols are not always the same once serialized and parsed again).</summary>
    private readonly Dictionary<string, List<CssComponentValue>> _descriptorValues;
    private string _name;

    internal CssCounterStyleRule(string name, Dictionary<string, string> descriptors, Dictionary<string, List<CssComponentValue>> descriptorValues)
    {
        _name = name;
        _descriptors = descriptors;
        _descriptorValues = descriptorValues;
    }

    /// <summary>The component values of a descriptor, without the whitespace; null when the rule does not have the descriptor.</summary>
    internal List<CssComponentValue>? GetDescriptorValue(string name) => _descriptorValues.TryGetValue(name, out var value) ? ValueMatcher.RemoveWhitespace(value) : null;

    /// <summary>
    /// Gets the legacy numeric type of the rule: <see cref="CssRule.CounterStyleRule"/> for <c>@counter-style</c> rules.
    /// </summary>
    public override ushort Type => CounterStyleRule;

    /// <summary>Gets or sets the name of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-name")]
    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (CssRuleParser.ParseCounterStyleName(CssParser.ParseListOfComponentValues(value)) is { } name)
            {
                _name = name;
                Cascade.CssStyleVersion.Increment(this);
            }
        }
    }

    private string GetDescriptor(string name) => _descriptors.GetValueOrDefault(name, "");

    /// <summary>
    /// https://drafts.csswg.org/css-counter-styles-3/#the-csscounterstylerule-interface: the value is ignored when it is invalid,
    /// when it changes the algorithm of the system (the first symbol value of a fixed system and the extended style may change),
    /// or when the rule would no longer define a counter style (e.g. a single symbol for an alphabetic system).
    /// </summary>
    private void SetDescriptor(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var values = CssParser.ParseListOfComponentValues(value);
        if (CssStyleDeclaration.ParseValue("@counter-style", name, values, originalText: null) is not [var parsed])
            return;

        var hasPrevious = _descriptors.TryGetValue(name, out var previous);
        if (name == "system" && GetSystemKeyword(parsed.Value) != GetSystemKeyword(previous))
            return;

        var previousValue = _descriptorValues.GetValueOrDefault(name);
        _descriptors[name] = parsed.Value;
        _descriptorValues[name] = values;
        if (name is "system" or "symbols" or "additive-symbols" && Cascade.CounterStyle.Create(this, scope: null, predefined: true, static _ => Cascade.CounterStyle.Decimal) is null)
        {
            if (hasPrevious)
            {
                _descriptors[name] = previous!;
                _descriptorValues[name] = previousValue!;
            }
            else
            {
                _descriptors.Remove(name);
                _descriptorValues.Remove(name);
            }

            return;
        }

        Cascade.CssStyleVersion.Increment(this);

        // The system is symbolic when the descriptor is omitted
        static string GetSystemKeyword(string? system)
        {
            if (string.IsNullOrEmpty(system))
                return "symbolic";

            var index = system.IndexOf(' ', StringComparison.Ordinal);
            return index < 0 ? system : system[..index];
        }
    }

    /// <summary>Gets or sets the <c>system</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-system"/>
    [IdlMember("system", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-system")]
    public string System
    {
        get => GetDescriptor("system");
        set => SetDescriptor("system", value);
    }

    /// <summary>Gets or sets the <c>symbols</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-symbols"/>
    [IdlMember("symbols", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-symbols")]
    public string Symbols
    {
        get => GetDescriptor("symbols");
        set => SetDescriptor("symbols", value);
    }

    /// <summary>Gets or sets the <c>additive-symbols</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-additivesymbols"/>
    [IdlMember("additiveSymbols", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-additivesymbols")]
    public string AdditiveSymbols
    {
        get => GetDescriptor("additive-symbols");
        set => SetDescriptor("additive-symbols", value);
    }

    /// <summary>Gets or sets the <c>negative</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-negative"/>
    [IdlMember("negative", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-negative")]
    public string Negative
    {
        get => GetDescriptor("negative");
        set => SetDescriptor("negative", value);
    }

    /// <summary>Gets or sets the <c>prefix</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-prefix"/>
    [IdlMember("prefix", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-prefix")]
    public string Prefix
    {
        get => GetDescriptor("prefix");
        set => SetDescriptor("prefix", value);
    }

    /// <summary>Gets or sets the <c>suffix</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-suffix"/>
    [IdlMember("suffix", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-suffix")]
    public string Suffix
    {
        get => GetDescriptor("suffix");
        set => SetDescriptor("suffix", value);
    }

    /// <summary>Gets or sets the <c>range</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-range"/>
    [IdlMember("range", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-range")]
    public string Range
    {
        get => GetDescriptor("range");
        set => SetDescriptor("range", value);
    }

    /// <summary>Gets or sets the <c>pad</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-pad"/>
    [IdlMember("pad", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-pad")]
    public string Pad
    {
        get => GetDescriptor("pad");
        set => SetDescriptor("pad", value);
    }

    /// <summary>Gets or sets the <c>speak-as</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-speakas"/>
    [IdlMember("speakAs", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-speakas")]
    public string SpeakAs
    {
        get => GetDescriptor("speak-as");
        set => SetDescriptor("speak-as", value);
    }

    /// <summary>Gets or sets the <c>fallback</c> descriptor of the counter style.</summary>
    /// <seealso href="https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-fallback"/>
    [IdlMember("fallback", "https://drafts.csswg.org/css-counter-styles-3/#dom-csscounterstylerule-fallback")]
    public string Fallback
    {
        get => GetDescriptor("fallback");
        set => SetDescriptor("fallback", value);
    }

    internal override string GetPrelude() => "@counter-style " + CssSerializer.SerializeIdentifier(_name);

    internal override List<CssSerializedDeclaration> GetDeclarations() => [.. _descriptors.Select(d => new CssSerializedDeclaration(d.Key, d.Value, Important: false))];
}

/// <summary>https://drafts.csswg.org/css-fonts-4/#om-fontpalettevalues</summary>
/// <seealso href="https://drafts.csswg.org/css-fonts-4/#cssfontpalettevaluesrule"/>
[IdlInterface("CSSFontPaletteValuesRule", "https://drafts.csswg.org/css-fonts-4/#cssfontpalettevaluesrule")]
public sealed class CssFontPaletteValuesRule : CssRule
{
    private readonly Dictionary<string, string> _descriptors;

    internal CssFontPaletteValuesRule(string name, Dictionary<string, string> descriptors)
    {
        Name = name;
        _descriptors = descriptors;
    }

    /// <summary>Gets the name of the palette values, including the leading <c>--</c>.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-name")]
    public string Name { get; }

    /// <summary>Gets the <c>font-family</c> descriptor of the rule, or an empty string when it is not set.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-fontfamily"/>
    [IdlMember("fontFamily", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-fontfamily")]
    public string FontFamily => _descriptors.GetValueOrDefault("font-family", "");

    /// <summary>Gets the <c>base-palette</c> descriptor of the rule, or an empty string when it is not set.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-basepalette"/>
    [IdlMember("basePalette", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-basepalette")]
    public string BasePalette => _descriptors.GetValueOrDefault("base-palette", "");

    /// <summary>
    /// Gets the <c>override-colors</c> descriptor of the rule, or an empty string when it is not set.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-overridecolors"/>
    [IdlMember("overrideColors", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontpalettevaluesrule-overridecolors")]
    public string OverrideColors => _descriptors.GetValueOrDefault("override-colors", "");

    /// <summary>The names of the font families the palette applies to (the <c>font-family</c> descriptor).</summary>
    internal IReadOnlyList<string> Families => field ??= FontFaceSet.ParseFamilies(FontFamily);

    internal override string GetPrelude() => "@font-palette-values " + CssSerializer.SerializeIdentifier(Name);

    internal override List<CssSerializedDeclaration> GetDeclarations() => [.. _descriptors.Select(d => new CssSerializedDeclaration(d.Key, d.Value, Important: false))];
}
