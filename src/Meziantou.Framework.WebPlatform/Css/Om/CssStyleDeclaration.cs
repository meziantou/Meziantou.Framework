using System.Collections;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>A CSS declaration (https://drafts.csswg.org/cssom/#css-declaration).</summary>
internal sealed class CssDeclarationEntry(CssLonghandValue value, bool important)
{
    private CssLonghandValue _value = value;
    private CssLonghandSnapshot? _snapshot;

    public CssLonghandValue Value
    {
        get => _value;
        set
        {
            _value = value;
            _snapshot = null;
        }
    }

    /// <summary>An immutable copy of the value, shared by the cascaded values of the elements the declaration applies to (a new one is created when the value changes).</summary>
    public CssLonghandSnapshot Snapshot => _snapshot ??= new CssLonghandSnapshot(_value);

    public string Name => Value.Name;

    public bool Important { get; set; } = important;
}

/// <summary>
/// A declaration as written by https://drafts.csswg.org/cssom/#serialize-a-css-declaration-block: <paramref name="Name"/> is
/// the serialized property name (a shorthand when the longhands are serialized together), <paramref name="Value"/> its
/// serialized value.
/// </summary>
internal readonly record struct CssSerializedDeclaration(string Name, string Value, bool Important);

/// <summary>https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssstyledeclaration"/>
[IdlInterface("CSSStyleDeclaration", "https://drafts.csswg.org/cssom/#cssstyledeclaration")]
public partial class CssStyleDeclaration : IReadOnlyList<string>
{
    private readonly List<CssDeclarationEntry> _declarations;
    private readonly Element? _ownerNode;
    private readonly CssRule? _parentRule;

    /// <summary>The at-rule whose descriptors are declared (e.g. "@font-face"), or null for properties.</summary>
    private readonly string? _atRule;

    /// <summary>The value of the style attribute the declarations were parsed from (the owner node's declarations are updated lazily).</summary>
    private string? _source;

    /// <summary>Creates the declarations of the style attribute of an element (https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style).</summary>
    internal CssStyleDeclaration(Element ownerNode)
    {
        _declarations = [];
        _ownerNode = ownerNode;
    }

    /// <summary>Creates the declarations of a rule (https://drafts.csswg.org/cssom/#dom-cssstylerule-style).</summary>
    internal CssStyleDeclaration(CssRule? parentRule, List<CssDeclarationEntry>? declarations = null, string? atRule = null)
    {
        _parentRule = parentRule;
        _atRule = atRule;

        // The rule parser fills the declarations after the rule is created
        _declarations = declarations ?? [];
    }

    /// <summary>https://drafts.csswg.org/cssom/#cssstyledeclaration-readonly-flag</summary>
    internal bool IsReadOnly { get; init; }

    /// <summary>The declarations of a computed style (https://drafts.csswg.org/cssom/#cssstyledeclaration-computed-flag), or null.</summary>
    internal Cascade.ComputedStyleDeclarations? Computed { get; init; }

    /// <summary>The owner node (https://drafts.csswg.org/cssom/#cssstyledeclaration-owner-node).</summary>
    internal Element? OwnerNode => _ownerNode;

    /// <summary>The declarations (https://drafts.csswg.org/cssom/#cssstyledeclaration-declarations).</summary>
    internal List<CssDeclarationEntry> Declarations
    {
        get
        {
            if (_ownerNode is not null)
            {
                var value = _ownerNode.GetAttributeValue("style");
                if (!ReferenceEquals(value, _source))
                {
                    // The attribute changed and the updating flag is unset: the declarations are reparsed, unless the value is
                    // blocked by a content security policy (https://html.spec.whatwg.org/multipage/dom.html#the-style-attribute)
                    _declarations.Clear();
                    if (value is not null && !ReferenceEquals(value, (_ownerNode.RareDataOrNull as Dom.ElementRareData)?.BlockedStyleAttribute))
                    {
                        ParseDeclarationBlock(value, _declarations, atRule: null, IsQuirksMode);
                    }

                    _source = value;
                }
            }

            return _declarations;
        }
    }

    /// <summary>
    /// Gets or sets the serialization of the declaration block. Setting the value replaces all the declarations.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-csstext"/>
    [IdlMember("cssText", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-csstext")]
    public string CssText
    {
        // The serialization of a computed style is the empty string
        get => Computed is not null ? "" : SerializeDeclarationBlock(Declarations);
        set
        {
            ThrowIfReadOnly();
            var declarations = Declarations;
            declarations.Clear();
            ParseDeclarationBlock(value ?? "", declarations, _atRule, IsQuirksMode);
            UpdateStyleAttribute();
        }
    }

    /// <summary>Gets the number of declarations in the block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-length"/>
    [IdlMember("length", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-length")]
    public uint Length => (uint)Count;

    /// <summary>Gets the number of declarations in the block.</summary>
    [NotInIdl]
    public int Count => Computed?.Count ?? Declarations.Count;

    /// <summary>
    /// Returns the name of the property at the given index, or an empty string when the index is out of range.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-item"/>
    [IdlMember("item", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-item")]
    public string Item(uint index)
    {
        if (Computed is { } computed)
            return index < (uint)computed.Count ? computed.GetName((int)index) : "";

        var declarations = Declarations;
        return index < (uint)declarations.Count ? declarations[(int)index].Name : "";
    }

    /// <summary>
    /// Gets the name of the property at the given index, or an empty string when the index is out of range.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-item"/>
    [IdlMember("getter", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[uint index] => Item(index);

    /// <summary>Gets the name of the property at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[int index] => Computed is { } computed ? computed.GetName(index) : Declarations[index].Name;

    /// <summary>Gets the rule that contains the declaration block, or null when the block is not in a rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-parentrule"/>
    [IdlMember("parentRule", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-parentrule")]
    public CssRule? ParentRule => _parentRule;

    private static string NormalizePropertyName(string property) => CssPropertyRegistry.NormalizeName(property);

    private CssDeclarationEntry? Find(string name) => FindByName(Declarations, name);

    /// <summary>
    /// Returns a function that finds the declaration of a property, for <paramref name="lookupCount"/> lookups (the longhands
    /// of a shorthand): a large block is indexed when there are many lookups (e.g. the ~600 longhands of all), so that
    /// looking up all the longhands is not quadratic.
    /// </summary>
    private static Func<string, CssDeclarationEntry?> CreateFinder(List<CssDeclarationEntry> declarations, int lookupCount)
    {
        if (lookupCount <= 16 || declarations.Count <= 16)
            return name => FindByName(declarations, name);

        var byName = new Dictionary<string, CssDeclarationEntry>(declarations.Count, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            byName.TryAdd(declaration.Name, declaration);
        }

        return name => byName.GetValueOrDefault(name);
    }

    /// <summary>Returns the value of the given property, or an empty string when the property is not set.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-getpropertyvalue"/>
    [IdlMember("getPropertyValue", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-getpropertyvalue")]
    public string GetPropertyValue(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        property = NormalizePropertyName(property);
        if (Computed is { } computed)
            return computed.GetPropertyValue(property);
        if (GetShorthand(property) is { } shorthand)
        {
            var longhands = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
            var find = CreateFinder(Declarations, longhands.Count);
            bool? important = null;
            foreach (var longhand in longhands)
            {
                var declaration = find(longhand);
                if (declaration is null)
                    return "";

                if (important is not null && important != declaration.Important)
                    return "";

                important = declaration.Important;
            }

            return CssShorthands.Serialize(shorthand, name => find(name)?.Value);
        }

        return Find(ResolveAlias(property))?.Value.Value ?? "";
    }

    /// <summary>
    /// Returns the value of a longhand as specified, when its text differs from the serialized value returned by
    /// <see cref="GetPropertyValue"/> (see <see cref="CssLonghandValue.TypedValue"/>), or null.
    /// </summary>
    /// <param name="isRepresentation">Whether the value is the internal representation of values set through the Typed OM (see <see cref="CssLonghandValue.TypedValueIsRepresentation"/>).</param>
    internal string? GetTypedValue(string property, out bool isRepresentation)
    {
        isRepresentation = false;
        property = NormalizePropertyName(property);
        if (Computed is not null || GetShorthand(property) is not null)
            return null;

        if (Find(ResolveAlias(property)) is not { Value: { TypedValue: { } typedValue } value })
            return null;

        isRepresentation = value.TypedValueIsRepresentation;
        return typedValue;
    }

    /// <summary>The legacy aliases (e.g. -webkit-transform) are stored under the name of the property they alias.</summary>
    private string ResolveAlias(string property)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return property;

        if (_atRule is not null && CssPropertyRegistry.GetDescriptor(_atRule, property) is { } descriptor)
            return descriptor.Name;

        return CssPropertyRegistry.Get(property)?.Name ?? property;
    }

    /// <summary>
    /// Returns the priority of the given property (<c>important</c>), or an empty string when it has no priority.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-getpropertypriority"/>
    [IdlMember("getPropertyPriority", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-getpropertypriority")]
    public string GetPropertyPriority(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        property = NormalizePropertyName(property);
        if (Computed is not null)
            return "";
        if (GetShorthand(property) is { } shorthand)
        {
            var longhands = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
            var find = CreateFinder(Declarations, longhands.Count);
            foreach (var longhand in longhands)
            {
                if (find(longhand) is not { Important: true })
                    return "";
            }

            return "important";
        }

        return Find(ResolveAlias(property)) is { Important: true } ? "important" : "";
    }

    /// <summary>Sets the value and the priority of the given property. An empty value removes the property.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty"/>
    [IdlMember("setProperty", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty")]
    public void SetProperty(string property, string? value, string? priority = "")
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly();
        value ??= "";
        priority ??= "";
        if (!CssPropertyRegistry.IsCustomProperty(property))
        {
            property = InfraAscii.ToAsciiLowercase(property);
        }
        else if (!IsValidCustomPropertyName(property))
        {
            // The name of a custom property is a <dashed-ident> (https://drafts.csswg.org/css-variables/#custom-property),
            // e.g. it cannot contain whitespace
            return;
        }

        if (!IsSupported(_atRule, property))
            return;

        if (value.Length == 0)
        {
            RemoveProperty(property);
            return;
        }

        if (priority.Length != 0 && (!InfraAscii.EqualsIgnoreAsciiCase(priority, "important") || _atRule is FunctionContext or PositionTryContext))
            return;

        var values = ParsePropertyValue(_atRule, property, value, IsQuirksMode);
        if (values is null)
            return;

        var important = priority.Length != 0;
        var updated = false;
        var index = CreateIndex(Declarations, values.Count);
        foreach (var longhand in values)
        {
            updated |= SetDeclaration(longhand, important, index);
        }

        if (updated)
        {
            UpdateStyleAttribute();
        }
    }

    /// <summary>
    /// Sets a property to a computed value (https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles). The
    /// declared value keeps the case of the computed value where the serialization of a specified value differs only by the
    /// case (the names of the transform functions, e.g. <c>scaleX(1.5)</c>, serialized <c>scalex(1.5)</c> when specified), as in
    /// the browsers, whose declared value is the computed value itself.
    /// </summary>
    internal void SetComputedProperty(string property, string value)
    {
        SetProperty(property, value);
        if (CssPropertyRegistry.IsCustomProperty(property) || Find(ResolveAlias(InfraAscii.ToAsciiLowercase(property))) is not { } declaration)
            return;

        var declared = declaration.Value;
        if (declared.ShorthandName is null && !string.Equals(declared.Value, value, StringComparison.Ordinal) && string.Equals(declared.Value, value, StringComparison.OrdinalIgnoreCase))
        {
            declaration.Value = declared with { Value = value, TypedValue = null };
            UpdateStyleAttribute();
        }
    }

    /// <summary>Returns whether the name of a custom property only contains identifier code points (the non-ASCII code points are not checked).</summary>
    private static bool IsValidCustomPropertyName(string name)
    {
        foreach (var c in name)
        {
            if (c < 0x80 && !CssTokenizer.IsIdentCodePoint(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Parses the value of a property set by <see cref="SetProperty"/> (https://drafts.csswg.org/cssom/#parse-a-css-value). As in
    /// a style sheet, the original text spans from the first token to the last one: the whitespace and the comments around them
    /// are not part of it (https://drafts.csswg.org/css-syntax-3/#consume-declaration).
    /// </summary>
    private static List<CssLonghandValue>? ParsePropertyValue(string? atRule, string property, string value, bool quirks = false)
    {
        var values = CssParser.ParseListOfComponentValues(value, out var preprocessed);
        var first = values.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        var last = values.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        var originalText = first < 0 ? "" : preprocessed[values[first].Start..Math.Min(values[last].End, preprocessed.Length)];

        return ParseValue(atRule, property, values, originalText, preprocessed) ?? (quirks ? ParseQuirkyValue(atRule, NormalizePropertyName(property), values) : null);
    }

    /// <summary>
    /// Parses a value that is invalid in standards mode as a value of a document in quirks mode, where the quirky lengths
    /// (https://quirks.spec.whatwg.org/#the-unitless-length-quirk) and the quirky colors
    /// (https://quirks.spec.whatwg.org/#the-hashless-hex-color-quirk) are valid in some properties. Returns null when the value
    /// is still invalid.
    /// </summary>
    private static List<CssLonghandValue>? ParseQuirkyValue(string? atRule, string name, List<CssComponentValue> values)
        => atRule is null && CssQuirks.RewriteQuirkyValues(name, values) is { } rewritten ? ParseValue(atRule, name, rewritten, originalText: null) : null;

    /// <summary>
    /// Whether the declarations are parsed in quirks mode: the declarations of the style attribute of an element of a
    /// document in quirks mode, or of a rule of a style sheet parsed in quirks mode.
    /// </summary>
    private bool IsQuirksMode => _ownerNode is not null ? _ownerNode.NodeDocument.Mode == DocumentMode.Quirks : _parentRule?.ParentStyleSheet?.IsQuirksMode == true;

    /// <summary>The context of the declarations of a keyframe (the animation properties do not apply).</summary>
    internal const string KeyframeContext = "@keyframe";

    /// <summary>
    /// The context of the declarations of a @function rule (https://drafts.csswg.org/css-mixins-1/#function-body): the
    /// custom properties (the local variables) and the result descriptor; !important declarations are invalid.
    /// </summary>
    internal const string FunctionContext = "@function";

    /// <summary>
    /// The context of the declarations of a @position-try rule (https://drafts.csswg.org/css-anchor-position-1/#fallback-rule): the
    /// accepted @position-try properties; !important declarations are invalid.
    /// </summary>
    internal const string PositionTryContext = "@position-try";

    /// <summary>
    /// Returns whether a property is an accepted @position-try property (https://drafts.csswg.org/css-anchor-position-1/#accepted-position-try-properties):
    /// the inset, margin, sizing and self-alignment properties, position-anchor and position-area.
    /// </summary>
    internal static bool IsAcceptedPositionTryProperty(string property)
    {
        var name = CssPropertyRegistry.Get(CssPropertyRegistry.NormalizeName(property))?.Name ?? property;
        return name is "top" or "right" or "bottom" or "left" or "position-anchor" or "position-area" or "align-self" or "justify-self" or "place-self"
            or "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height"
            or "block-size" or "inline-size" or "min-block-size" or "min-inline-size" or "max-block-size" or "max-inline-size"
            || name.StartsWith("inset", StringComparison.Ordinal) || name.StartsWith("margin", StringComparison.Ordinal);
    }

    /// <summary>Returns whether the property applies in a keyframe (https://drafts.csswg.org/css-animations/#keyframes).</summary>
    internal static bool IsAllowedInKeyframe(string property)
    {
        var name = CssPropertyRegistry.NormalizeName(property);
        name = CssPropertyRegistry.Get(name)?.Name ?? name;
        return !name.StartsWith("animation", StringComparison.Ordinal) || name is "animation-timing-function" or "animation-composition";
    }

    /// <summary>
    /// Returns whether the property applies to the page context (https://drafts.csswg.org/css-page-3/#page-properties): the
    /// margins, borders, paddings, backgrounds, outlines and size of the page box, the counters, the properties inherited by the
    /// page-margin boxes. The other declarations of @page are kept (as in the browsers, the CSSOM shows them) but ignored by
    /// the cascade of the page context (see <see cref="Cascade.StyleEngine.GetPageStyle"/>).
    /// </summary>
    internal static bool AppliesToPageContext(string property)
    {
        var name = CssPropertyRegistry.Get(property)?.Name ?? property;
        return CssPropertyRegistry.IsCustomProperty(name) || name.StartsWith("margin", StringComparison.Ordinal) || name.StartsWith("padding", StringComparison.Ordinal)
            || name.StartsWith("border", StringComparison.Ordinal) || name.StartsWith("background", StringComparison.Ordinal) || name.StartsWith("font", StringComparison.Ordinal)
            || name.StartsWith("counter-", StringComparison.Ordinal) || name.StartsWith("outline", StringComparison.Ordinal) || name.StartsWith("text-", StringComparison.Ordinal)
            || name.StartsWith("white-space", StringComparison.Ordinal)
            || name is "color" or "line-height" or "direction" or "writing-mode" or "unicode-bidi" or "visibility" or "quotes" or "letter-spacing" or "word-spacing"
                or "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or "inline-size" or "block-size" or "min-inline-size"
                or "min-block-size" or "max-inline-size" or "max-block-size" or "box-sizing" or "box-shadow" or "size" or "page-orientation";
    }

    /// <summary>
    /// Returns whether the property applies to the page-margin boxes (https://drafts.csswg.org/css-page-3/#margin-properties):
    /// the properties of the page context, and the properties of the generated content and of the text.
    /// </summary>
    internal static bool AppliesToMarginBox(string property)
    {
        var name = CssPropertyRegistry.Get(property)?.Name ?? property;
        return AppliesToPageContext(name) || name.StartsWith("overflow", StringComparison.Ordinal)
            || name is "content" or "vertical-align" or "alignment-baseline" or "baseline-shift" or "z-index";
    }

    /// <summary>
    /// Returns whether the context accepts the property (e.g. the margin properties in @page). The declarations of @page and of
    /// the margin rules accept all the properties, as in Blink (WPT css/css-page/cssom/margin-001.html): the properties that
    /// do not apply there are ignored by their cascade.
    /// </summary>
    private static bool AcceptsProperty(string? atRule, string name) => atRule switch
    {
        null or "@page" => true,
        KeyframeContext => IsAllowedInKeyframe(name),
        FunctionContext => CssPropertyRegistry.IsCustomProperty(name),
        PositionTryContext => IsAcceptedPositionTryProperty(name),
        _ => atRule.Length > 1 && CssRuleParser.IsMarginRuleName(atRule[1..]),
    };

    /// <summary>Returns whether the property or descriptor is supported in the declarations.</summary>
    private static bool IsSupported(string? atRule, string name)
    {
        if (atRule is not null && CssPropertyRegistry.GetDescriptor(atRule, name) is not null)
            return true;

        return AcceptsProperty(atRule, name) && (CssPropertyRegistry.IsCustomProperty(name) || CssPropertyRegistry.Get(name) is not null);
    }

    /// <summary>Returns the shorthand property, unless the name is a descriptor of the at-rule (e.g. size in @page).</summary>
    private CssPropertyDefinition? GetShorthand(string property)
    {
        if (CssPropertyRegistry.IsCustomProperty(property) || (_atRule is not null && CssPropertyRegistry.GetDescriptor(_atRule, property) is not null))
            return null;

        return CssPropertyRegistry.Get(property) is { IsShorthand: true } shorthand ? shorthand : null;
    }

    /// <summary>Parses the value of a property or of a descriptor of the at-rule; returns null when it is invalid.</summary>
    internal static List<CssLonghandValue>? ParseValue(string? atRule, string name, List<CssComponentValue> values, string? originalText, string? source = null)
    {
        // The result descriptor of @function is parsed like a custom property (https://drafts.csswg.org/css-mixins-1/#the-result-descriptor)
        if (atRule == FunctionContext && name == "result")
            return CssShorthands.ParseCustomPropertyValue(name, values, originalText);

        // The tree counting functions and random() need an element (https://drafts.csswg.org/css-values-5/#tree-counting,
        // https://drafts.csswg.org/css-values-5/#random): they are invalid in the descriptors and in the page context
        if (atRule is not null and not (KeyframeContext or FunctionContext or PositionTryContext) && ContainsElementDependentFunction(values))
            return null;

        if (atRule is not null && CssPropertyRegistry.GetDescriptor(atRule, name) is { } descriptor)
        {
            var significant = ValueMatcher.RemoveWhitespace(values);
            if (significant.Count == 0)
                return null;

            // <urange># (https://drafts.csswg.org/css-syntax-3/#urange-syntax)
            if (descriptor.Grammar.StartsWith("<unicode-range-token>", StringComparison.Ordinal) || descriptor.Name == "unicode-range")
                return source is not null && CssUnicodeRanges.Parse(values, source) is { } ranges ? [new CssLonghandValue(descriptor.Name, ranges)] : null;

            if (descriptor.Grammar == "<font-src-list>")
                return ParseFontSourceList(values) is { } sources ? [new CssLonghandValue(descriptor.Name, sources)] : null;

            // https://drafts.csswg.org/css-values-5/#tree-counting: sibling-index() and sibling-count() are invalid outside an element context
            if (CssPropertyRegistry.GetGrammar(descriptor) is { } grammar && (!ValueMatcher.Matches(grammar, significant) || significant.Exists(value => value is CssFunction function && ValueMatcher.IsTreeCountingFunction(function))))
                return null;

            if (!CssPropertyRules.IsValidDescriptor(descriptor, significant))
                return null;

            var serialized = CssPropertyRules.CanonicalizeDescriptor(descriptor, significant) ?? CssSerializer.SerializeValue(values, property: null);
            if (atRule == "@counter-style")
            {
                serialized = SerializeCounterStyleDescriptor(descriptor.Name, significant, serialized);
            }
            else if (atRule == "@font-face")
            {
                serialized = SerializeFontFaceRange(descriptor.Name, significant, serialized);
            }

            if (serialized is null)
                return null;

            return [new CssLonghandValue(descriptor.Name, CssSerializer.SerializeFontValue(descriptor.Name, serialized) ?? serialized)];
        }

        if (!AcceptsProperty(atRule, name))
            return null;

        return CssShorthands.ParseValue(name, values, originalText);
    }

    private static string TrimWhitespace(ReadOnlySpan<char> value) => value.Trim(" \t\n").ToString();

    /// <summary>
    /// The descriptors of @counter-style whose grammar does not express all the constraints
    /// (https://drafts.csswg.org/css-counter-styles/#counter-style-range): the lower bound of a range is not higher than its
    /// upper bound, and the weights of additive-symbols are in strictly decreasing order. The weight of pad and
    /// additive-symbols is serialized before the symbol (the order of the grammar). Returns null when the value is invalid.
    /// </summary>
    private static string? SerializeCounterStyleDescriptor(string name, List<CssComponentValue> significant, string serialized)
    {
        switch (name)
        {
            case "range":
                foreach (var range in ValueMatcher.SplitAtCommas(significant))
                {
                    if (range is [CssPreservedToken { Type: CssTokenType.Number } lower, CssPreservedToken { Type: CssTokenType.Number } upper] && lower.Token.NumericValue > upper.Token.NumericValue)
                        return null;
                }

                return serialized;

            case "pad" or "additive-symbols":
            {
                var items = new List<string>();
                double? previousWeight = null;
                foreach (var item in ValueMatcher.SplitAtCommas(significant))
                {
                    if (item is not [var first, var second])
                        return serialized;

                    var (weight, symbol) = IsSymbol(first) ? (second, first) : (first, second);
                    if (name == "additive-symbols" && weight is CssPreservedToken { Type: CssTokenType.Number } number)
                    {
                        if (previousWeight <= number.Token.NumericValue)
                            return null;

                        previousWeight = number.Token.NumericValue;
                    }

                    items.Add(CssSerializer.SerializeValue([weight], property: null) + " " + CssSerializer.SerializeValue([symbol], property: null));
                }

                return string.Join(", ", items);
            }

            default:
                return serialized;
        }

        // <symbol> = <string> | <image> | <custom-ident>
        static bool IsSymbol(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.String or CssTokenType.Ident or CssTokenType.Url }
            || (value is CssFunction function && !CssCalc.IsMathFunction(function.Name));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts/#font-prop-desc: a range of font-weight or font-width (font-stretch) whose bounds are equal is
    /// serialized as a single value, and an oblique angle of zero (or a range of zero angles) is normal, like the browsers.
    /// </summary>
    private static string? SerializeFontFaceRange(string name, List<CssComponentValue> significant, string serialized)
    {
        switch (name)
        {
            case "font-weight" or "font-width" when significant is [CssPreservedToken lower, CssPreservedToken upper] && lower.Token.ToString() == upper.Token.ToString():
                return CssSerializer.SerializeValue([lower], property: null);

            case "font-style" when significant is [CssPreservedToken { Type: CssTokenType.Ident } oblique, CssPreservedToken { Type: CssTokenType.Dimension } angle, ..] && InfraAscii.EqualsIgnoreAsciiCase(oblique.Token.Value, "oblique"):
                if (significant is [_, _, CssPreservedToken upperAngle] && upperAngle.Token.ToString() != angle.Token.ToString())
                    return serialized;

                return angle.Token.NumericValue == 0 ? "normal" : "oblique " + CssSerializer.SerializeValue([angle], property: null);

            default:
                return serialized;
        }
    }

    private static bool ContainsElementDependentFunction(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when InfraAscii.ToAsciiLowercase(function.Name) is "sibling-index" or "sibling-count" or "random" or "random-item" || ContainsElementDependentFunction(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsElementDependentFunction(block.Value):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts/#font-face-src-parsing: the entries of the list that do not match &lt;font-src&gt;
    /// are dropped (also the entries whose format() string is not a supported format), and the descriptor is invalid when no
    /// entry is left. The keywords of format() and tech() are serialized in lowercase, the name of local() as a string.
    /// </summary>
    private static string? ParseFontSourceList(List<CssComponentValue> values)
    {
        if (CssPropertyRegistry.GetTypeGrammar("<font-src>") is not { } grammar)
            return null;

        var sources = new List<string>();
        foreach (var entry in Cascade.CssValueComputer.SplitArguments(values))
        {
            if (entry.Count > 0 && (ValueMatcher.Matches(grammar, entry) || IsLocalWithIdentifiers(entry)) && !HasUnsupportedFormatString(entry))
            {
                sources.Add(string.Join(' ', entry.Select(SerializeFontSourceComponent)));
            }
        }

        return sources.Count == 0 ? null : string.Join(", ", sources);

        // A family name of several identifiers may contain the CSS-wide keywords and default (local(default A)), which the
        // <custom-ident> of the grammar excludes
        static bool IsLocalWithIdentifiers(List<CssComponentValue> entry) => entry is [CssFunction function]
            && InfraAscii.ToAsciiLowercase(function.Name) == "local"
            && ValueMatcher.RemoveWhitespace(function.Value) is { Count: >= 2 } identifiers
            && identifiers.TrueForAll(value => value is CssPreservedToken { Type: CssTokenType.Ident });

        // https://drafts.csswg.org/css-fonts/#font-format-values: a format() string must be one of the format strings (the
        // -variations strings are the legacy forms of tech(variations)); an unsupported format string makes the entry invalid
        static bool HasUnsupportedFormatString(List<CssComponentValue> entry) => entry.Exists(static value => value is CssFunction function
            && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "format")
            && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } format]
            && format.Token.Value is not ("collection" or "embedded-opentype" or "opentype" or "svg" or "truetype" or "woff" or "woff2"
                or "opentype-variations" or "truetype-variations" or "woff-variations" or "woff2-variations"));

        static string SerializeFontSourceComponent(CssComponentValue value)
        {
            var name = value is CssFunction function ? InfraAscii.ToAsciiLowercase(function.Name) : null;
            if (name is not ("format" or "tech" or "local"))
                return CssSerializer.SerializeValue([value], property: null);

            // https://drafts.csswg.org/cssom/#serialize-a-local: local() is serialized with a string, as in the browsers (WPT
            // css/css-fonts/font-family-src-quoted.html)
            if (name == "local" && ValueMatcher.RemoveWhitespace(((CssFunction)value).Value) is { Count: > 0 } localName && localName.TrueForAll(static argument => argument is CssPreservedToken { Type: CssTokenType.Ident }))
                return "local(" + CssSerializer.SerializeString(string.Join(' ', localName.Select(static argument => ((CssPreservedToken)argument).Token.Value))) + ")";

            var arguments = Cascade.CssValueComputer.SplitArguments(((CssFunction)value).Value).Select(argument => string.Join(' ', argument.Select(component =>
                name != "local" && component is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : CssSerializer.SerializeValue([component], property: null))));
            return name + "(" + string.Join(", ", arguments) + ")";
        }
    }

    /// <summary>
    /// Fixes the original text of a value that ends at the end of the input
    /// (https://drafts.csswg.org/css-syntax-3/#consume-escaped-code-point): an escaped EOF is U+FFFD (it is ignored in
    /// strings), and the unclosed strings, urls, functions and blocks are closed.
    /// </summary>
    private static string FixupEndOfFile(string text, CssComponentValue last, string source)
    {
        var closers = new System.Text.StringBuilder();
        var value = last;
        while (true)
        {
            if (value is CssFunction function)
            {
                // The function is unclosed when its contents reach the end of the input (the ")" at the end of the input can
                // close a nested function, e.g. "f(g(a)")
                if (!source.EndsWith(')', StringComparison.Ordinal) || (function.Value.Count > 0 && function.Value[^1].End >= source.Length))
                {
                    closers.Insert(0, ')');
                }

                // Only the values that reach the end of the input can be unclosed (e.g. the {} block of "var({--x})" is closed)
                if (function.Value.Count == 0 || function.Value[^1].End < source.Length)
                    break;

                value = function.Value[^1];
            }
            else if (value is CssSimpleBlock block)
            {
                var closer = block.AssociatedToken switch
                {
                    CssTokenType.LeftParenthesis => ')',
                    CssTokenType.LeftSquareBracket => ']',
                    _ => '}',
                };

                if (!source.EndsWith(closer, StringComparison.Ordinal) || (block.Value.Count > 0 && block.Value[^1].End >= source.Length))
                {
                    closers.Insert(0, closer);
                }

                if (block.Value.Count == 0 || block.Value[^1].End < source.Length)
                    break;

                value = block.Value[^1];
            }
            else
            {
                break;
            }
        }

        if (value is CssPreservedToken { Token: var token } preserved && preserved.End >= source.Length)
        {
            var escapedEof = text.EndsWith('\\', StringComparison.Ordinal) && CountTrailingBackslashes(text) % 2 == 1;
            switch (token.Type)
            {
                case CssTokenType.String or CssTokenType.BadString:
                {
                    var quote = source[preserved.Start];
                    if (escapedEof)
                    {
                        text = text[..^1];
                    }

                    if (!text.EndsWith(quote, StringComparison.Ordinal) || text.Length == 1 || escapedEof || preserved.Start == source.Length - 1)
                    {
                        text += quote;
                    }

                    break;
                }

                case CssTokenType.Url:
                    if (escapedEof)
                    {
                        text = text[..^1] + "\uFFFD";
                    }

                    if (!text.EndsWith(')', StringComparison.Ordinal))
                    {
                        text += ")";
                    }

                    break;
                default:
                    if (escapedEof)
                    {
                        text = text[..^1] + "\uFFFD";
                    }

                    break;
            }
        }

        return text + closers;
    }

    private static int CountTrailingBackslashes(string text)
    {
        var count = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == '\\'; i--)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#set-a-css-declaration: the declaration is updated in place, unless a later declaration
    /// of its logical property group has a different mapping logic (e.g. padding-top followed by padding-block-start): then
    /// it is moved to the end.
    /// </summary>
    /// <param name="index">The declarations by name (see <see cref="CreateIndex"/>), updated by the method, or null.</param>
    private bool SetDeclaration(CssLonghandValue value, bool important, Dictionary<string, CssDeclarationEntry>? index = null)
    {
        var declarations = Declarations;
        var declaration = index is not null ? index.GetValueOrDefault(value.Name) : FindByName(declarations, value.Name);
        if (declaration is not null)
        {
            if (!HasLaterDeclarationWithDifferentMappingLogic(declarations, declaration))
            {
                if (declaration.Value == value && declaration.Important == important)
                    return false;

                // The value as specified (reified by the Typed OM) is not part of the serialization of the declaration: the
                // declaration block is not updated when only it changes (e.g. 10PX then 10px)
                var updated = declaration.Important != important || declaration.Value with { TypedValue = null, TypedValueIsRepresentation = false } != value with { TypedValue = null, TypedValueIsRepresentation = false };
                declaration.Value = value;
                declaration.Important = important;
                return updated;
            }

            declarations.Remove(declaration);
        }

        var entry = new CssDeclarationEntry(value, important);
        declarations.Add(entry);
        index?[value.Name] = entry;
        return true;
    }

    /// <summary>
    /// Returns the declarations by name when <paramref name="count"/> declarations are about to be set in a large block (e.g.
    /// the ~600 longhands of all), so that finding each of them is not quadratic, or null.
    /// </summary>
    private static Dictionary<string, CssDeclarationEntry>? CreateIndex(List<CssDeclarationEntry> declarations, int count)
    {
        if (count <= 16 || declarations.Count <= 16)
            return null;

        var index = new Dictionary<string, CssDeclarationEntry>(declarations.Count + count, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            index.TryAdd(declaration.Name, declaration);
        }

        return index;
    }

    /// <summary>
    /// Returns whether a declaration after <paramref name="declaration"/> is in its logical property group, one flow-relative
    /// and the other physical.
    /// </summary>
    private static bool HasLaterDeclarationWithDifferentMappingLogic(List<CssDeclarationEntry> declarations, CssDeclarationEntry declaration)
    {
        // Most properties are not in a logical property group: the following declarations are not checked
        var property = declaration.Name;
        if (CssPropertyRegistry.Get(property) is not { LogicalGroup: { } group })
            return false;

        var isFlowRelative = Cascade.LogicalProperties.IsFlowRelative(property);
        for (var i = declarations.IndexOf(declaration) + 1; i < declarations.Count; i++)
        {
            var other = declarations[i].Name;
            if (CssPropertyRegistry.Get(other)?.LogicalGroup == group && Cascade.LogicalProperties.IsFlowRelative(other) != isFlowRelative)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the given property and returns its previous value, or an empty string when it was not set.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-removeproperty"/>
    [IdlMember("removeProperty", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-removeproperty")]
    public string RemoveProperty(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        ThrowIfReadOnly();
        property = NormalizePropertyName(property);
        var value = GetPropertyValue(property);
        if (RemoveDeclarations(property))
        {
            UpdateStyleAttribute();
        }

        return value;
    }

    /// <summary>
    /// Removes the declarations of a property, then sets the property to a value that is valid for it, so that its
    /// declarations are at the end of the block (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-set).
    /// The style attribute is updated once. The property is only removed when the value is the empty string.
    /// </summary>
    /// <param name="isTypedRepresentation">
    /// Whether the value is the internal representation of keywords, numeric values or transform values set through the
    /// Typed OM, which reifies them as they were set (see <see cref="CssLonghandValue.TypedValueIsRepresentation"/>).
    /// </param>
    internal void ReplaceProperty(string property, string value, bool isTypedRepresentation = false)
    {
        ThrowIfReadOnly();
        property = NormalizePropertyName(property);
        var updated = RemoveDeclarations(property);
        if (value.Length > 0 && ParsePropertyValue(_atRule, property, value) is { } values)
        {
            foreach (var longhand in values)
            {
                var declared = isTypedRepresentation && values.Count == 1 && longhand.ShorthandName is null ? longhand with { TypedValue = value, TypedValueIsRepresentation = true } : longhand;
                updated |= SetDeclaration(declared, important: false);
            }
        }

        if (updated)
        {
            UpdateStyleAttribute();
        }
    }

    /// <summary>Removes the declarations of a property (the longhands of a shorthand); returns whether a declaration was removed.</summary>
    private bool RemoveDeclarations(string property)
    {
        var removed = false;
        var declarations = Declarations;
        if (GetShorthand(property) is { } shorthand)
        {
            var longhands = new HashSet<string>(CssPropertyRegistry.GetLonghandsRecursive(shorthand), StringComparer.Ordinal);
            removed = declarations.RemoveAll(d => longhands.Contains(d.Name)) > 0;
        }
        else
        {
            var name = ResolveAlias(property);
            removed = declarations.RemoveAll(d => d.Name == name) > 0;
        }

        return removed;
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly)
            throw DomException.NoModificationAllowed("The declarations are read-only");
    }

    /// <summary>https://drafts.csswg.org/cssom/#update-style-attribute-for</summary>
    private void UpdateStyleAttribute()
    {
        // The declarations of a style attribute are updated with the attribute, which invalidates the computed styles
        if (_ownerNode is null)
        {
            Cascade.CssStyleVersion.Increment(_parentRule);
            _parentRule?.ParentStyleSheet?.RequestCssResourcesUpdate();
            return;
        }

        // The updating flag: the declarations are not reparsed from the new value of the attribute, which is not checked by
        // the content security policies
        var rareData = _ownerNode.ElementRareData;
        rareData.SkipStyleAttributeCheck = true;
        try
        {
            _ownerNode.SetAttributeValue("style", SerializeDeclarationBlock(_declarations));
        }
        finally
        {
            rareData.SkipStyleAttributeCheck = false;
        }

        _source = _ownerNode.GetAttributeValue("style");
    }

    /// <summary>
    /// Parses a CSS declaration block (https://drafts.csswg.org/cssom/#parse-a-css-declaration-block) and appends the
    /// valid declarations.
    /// </summary>
    /// <param name="quirks">Whether CSS is parsed in quirks mode: the quirky lengths and colors are valid.</param>
    internal static void ParseDeclarationBlock(string text, List<CssDeclarationEntry> declarations, string? atRule, bool quirks = false)
    {
        // A list of declarations (https://drafts.csswg.org/css-style-attr/#syntax): the rules are invalid, and anything
        // that is not a declaration is consumed up to the next semicolon
        foreach (var node in CssParser.ParseDeclarationList(text, out var preprocessed))
        {
            AppendDeclaration((CssDeclaration)node, preprocessed, declarations, atRule, quirks);
        }
    }

    /// <summary>Appends a declaration parsed from a style sheet or a style attribute, if it is valid.</summary>
    /// <param name="quirks">Whether CSS is parsed in quirks mode: the quirky lengths and colors are valid.</param>
    internal static void AppendDeclaration(CssDeclaration declaration, string source, List<CssDeclarationEntry> declarations, string? atRule, bool quirks = false)
    {
        // !important is invalid in the body of a @function rule (https://drafts.csswg.org/css-mixins-1/#function-body)
        // https://drafts.csswg.org/css-anchor-position-1/#fallback-rule: "It is invalid to use !important on the properties in
        // the <declaration-list>"
        if (declaration.Important && atRule is FunctionContext or PositionTryContext)
            return;

        var name = NormalizePropertyName(declaration.Name);
        var originalText = declaration.Value.Count == 0 ? "" : TrimWhitespace(source.AsSpan(declaration.Value[0].Start..Math.Min(declaration.Value[^1].End, source.Length)));
        var endOfFile = declaration.Value.Count > 0 && declaration.Value[^1].End >= source.Length;
        if (endOfFile)
        {
            originalText = FixupEndOfFile(originalText, declaration.Value[^1], source);
        }

        // An empty custom property (e.g. --x: ;) is valid (https://drafts.csswg.org/css-variables/#defining-variables): its
        // value is serialized as a space, like the browsers
        if (originalText.Length == 0 && CssPropertyRegistry.IsCustomProperty(name))
        {
            originalText = " ";
        }

        // The parsed value only depends on the text of the value: the declarations repeated in the style sheets and the style
        // attributes (e.g. display: none) are parsed once
        List<CssLonghandValue>? longhands;
        if (!endOfFile && originalText.Length <= ParsedValueCache.MaxTextLength)
        {
            var cache = ParsedValueCache.Declarations;
            if (!cache.TryGetValue(atRule, name, originalText, out longhands))
            {
                longhands = ParseValue(atRule, name, declaration.Value, originalText, source);
                cache.Add(atRule, name, originalText, longhands);
            }
        }
        else
        {
            longhands = ParseValue(atRule, name, declaration.Value, originalText, source);
        }

        // The cached values are the ones of the standards mode: the quirky values are only tried when the value is invalid
        if (longhands is null && quirks)
        {
            longhands = ParseQuirkyValue(atRule, name, declaration.Value);
        }

        if (longhands is null)
            return;

        foreach (var longhand in longhands)
        {
            var index = IndexOf(declarations, longhand.Name);
            if (index >= 0)
            {
                // An important declaration is not overridden by a later normal declaration
                if (declarations[index].Important && !declaration.Important)
                    continue;

                declarations.RemoveAt(index);
            }

            declarations.Add(new CssDeclarationEntry(longhand, declaration.Important));
        }
    }

    private static int IndexOf(List<CssDeclarationEntry> declarations, string name)
    {
        for (var i = 0; i < declarations.Count; i++)
        {
            if (declarations[i].Name == name)
                return i;
        }

        return -1;
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-css-declaration-block, written by <see cref="CssWriter"/>.</summary>
    internal static string SerializeDeclarationBlock(List<CssDeclarationEntry> declarations)
    {
        if (declarations.Count == 0)
            return "";

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new CssWriter(output).WriteDeclarationBlock(parentRule: null, declarations);
        return output.ToString();
    }

    /// <summary>
    /// Returns the declarations written by https://drafts.csswg.org/cssom/#serialize-a-css-declaration-block: the longhands
    /// that can be serialized together are replaced by their shorthand, and the custom property names are escaped.
    /// </summary>
    internal static List<CssSerializedDeclaration> GetSerializedDeclarations(List<CssDeclarationEntry> declarations)
    {
        var result = new List<CssSerializedDeclaration>(declarations.Count);
        if (declarations.Count == 0)
            return result;

        var alreadySerialized = new HashSet<string>(StringComparer.Ordinal);

        // The index of the first declaration of each property, and the declarations of the longhands of the shorthand being
        // checked (the lookups of the ~600 longhands of all in a large block would be quadratic)
        Dictionary<string, int>? indexesByName = null;
        bool[]? isLonghand = null;
        foreach (var declaration in declarations)
        {
            var property = declaration.Name;
            if (alreadySerialized.Contains(property))
                continue;

            var serialized = false;
            if (!CssPropertyRegistry.IsCustomProperty(property))
            {
                foreach (var shorthandName in CssPropertyRegistry.GetShorthands(property))
                {
                    // The size shorthand of css-sizing-4 (width and height) is not used by the browsers to serialize the
                    // declarations: "width: 1px; height: 2px" is not serialized as "size: 1px 2px"
                    if (shorthandName == "size" || CssPropertyRules.IsLegacyShorthand(shorthandName) || CssPropertyRegistry.Get(shorthandName) is not { } shorthand)
                        continue;

                    // All the longhands must be declared (e.g. the ~600 longhands of all are rarely declared)
                    var longhands = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
                    if (longhands.Count > declarations.Count)
                        continue;

                    if (indexesByName is null)
                    {
                        indexesByName = new Dictionary<string, int>(declarations.Count, StringComparer.Ordinal);
                        for (var i = 0; i < declarations.Count; i++)
                        {
                            indexesByName.TryAdd(declarations[i].Name, i);
                        }
                    }

                    if (!AreAllDeclared(longhands, indexesByName, alreadySerialized))
                        continue;

                    isLonghand ??= new bool[declarations.Count];
                    var current = new List<CssDeclarationEntry>(longhands.Count);
                    var minIndex = int.MaxValue;
                    var maxIndex = -1;
                    foreach (var longhand in longhands)
                    {
                        var index = indexesByName[longhand];
                        current.Add(declarations[index]);
                        isLonghand[index] = true;
                        minIndex = Math.Min(minIndex, index);
                        maxIndex = Math.Max(maxIndex, index);
                    }

                    var value = "";
                    var important = current[0].Important;
                    if (!current.Exists(d => d.Important != important)

                        // The longhands are not serialized as a shorthand when a declaration between them is in the logical
                        // property group of one of them and one of them is flow-relative, as in browsers (e.g. margin-top,
                        // margin-inline-start, margin-bottom)
                        && !HasDeclarationOfLogicalGroupBetween(declarations, isLonghand, current, minIndex, maxIndex))
                    {
                        value = CssShorthands.Serialize(shorthand, name => indexesByName.TryGetValue(name, out var index) && isLonghand[index] ? declarations[index].Value : null);
                    }

                    Array.Clear(isLonghand, minIndex, maxIndex - minIndex + 1);

                    if (value.Length == 0)
                        continue;

                    result.Add(new CssSerializedDeclaration(shorthandName, value, important));
                    foreach (var entry in current)
                    {
                        alreadySerialized.Add(entry.Name);
                    }

                    serialized = true;
                    break;
                }
            }

            if (serialized)
                continue;

            result.Add(new CssSerializedDeclaration(CssPropertyRegistry.IsCustomProperty(property) ? CssSerializer.SerializeIdentifier(property) : property, declaration.Value.Value, declaration.Important));
            alreadySerialized.Add(property);
        }

        return result;
    }

    /// <summary>Returns whether the longhands are declared and not serialized yet.</summary>
    private static bool AreAllDeclared(IReadOnlyList<string> longhands, Dictionary<string, int> indexesByName, HashSet<string> alreadySerialized)
    {
        foreach (var longhand in longhands)
        {
            if (alreadySerialized.Contains(longhand) || !indexesByName.ContainsKey(longhand))
                return false;
        }

        return true;
    }

    private static CssDeclarationEntry? FindByName(List<CssDeclarationEntry> declarations, string name)
    {
        foreach (var declaration in declarations)
        {
            if (declaration.Name == name)
                return declaration;
        }

        return null;
    }

    /// <param name="isLonghand">Whether each declaration is the declaration of one of <paramref name="longhands"/>.</param>
    private static bool HasDeclarationOfLogicalGroupBetween(List<CssDeclarationEntry> declarations, bool[] isLonghand, List<CssDeclarationEntry> longhands, int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            var declaration = declarations[i];
            if (isLonghand[i] || CssPropertyRegistry.Get(declaration.Name) is not { LogicalGroup: { } group })
                continue;

            var isFlowRelative = Cascade.LogicalProperties.IsFlowRelative(declaration.Name);
            foreach (var longhand in longhands)
            {
                if (CssPropertyRegistry.Get(longhand.Name)?.LogicalGroup == group && (isFlowRelative || Cascade.LogicalProperties.IsFlowRelative(longhand.Name)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Returns an enumerator over the names of the properties of the block.</summary>
    [NotInIdl]
    public IEnumerator<string> GetEnumerator()
    {
        if (Computed is { } computed)
        {
            foreach (var name in computed.Names)
            {
                yield return name;
            }

            yield break;
        }

        foreach (var declaration in Declarations)
        {
            yield return declaration.Name;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://drafts.csswg.org/cssom/#cssstyleproperties</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssstyleproperties"/>
[IdlInterface("CSSStyleProperties", "https://drafts.csswg.org/cssom/#cssstyleproperties")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case", Justification = "Some CSS properties only differ by a dash (stroke-dash-corner and stroke-dashcorner)")]
public sealed partial class CssStyleProperties : CssStyleDeclaration
{
    internal CssStyleProperties(Element ownerNode)
        : base(ownerNode)
    {
    }

    internal CssStyleProperties(CssRule? parentRule, List<CssDeclarationEntry>? declarations = null, string? context = null)
        : base(parentRule, declarations, context)
    {
    }

    /// <summary>
    /// Gets or sets the <c>float</c> property (<c>cssFloat</c> because <c>float</c> is a reserved word in JavaScript).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-cssfloat"/>
    [IdlMember("cssFloat", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-cssfloat")]
    public string CssFloat
    {
        get => GetPropertyValue("float");
        set => SetProperty("float", value ?? "");
    }

    // The properties that webref lists without grammar (see CssPropertyRegistry.SupplementalProperties)
    /// <summary>Gets or sets the <c>-webkit-box-orient</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxOrient", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxOrient", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-orient", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    public string WebkitBoxOrient
    {
        get => GetPropertyValue("-webkit-box-orient");
        set => SetProperty("-webkit-box-orient", value ?? "");
    }

    /// <summary>Gets or sets the <c>-webkit-box-align</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxAlign", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxAlign", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-align", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    public string WebkitBoxAlign
    {
        get => GetPropertyValue("-webkit-box-align");
        set => SetProperty("-webkit-box-align", value ?? "");
    }

    /// <summary>Gets or sets the <c>-webkit-box-direction</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxDirection", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxDirection", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-direction", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    [Experimental(ExperimentalApis.NonStandardCssProperties, UrlFormat = ExperimentalApis.UrlFormat)]
    public string WebkitBoxDirection
    {
        get => GetPropertyValue("-webkit-box-direction");
        set => SetProperty("-webkit-box-direction", value ?? "");
    }

    /// <summary>Gets or sets the <c>-webkit-box-flex</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxFlex", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxFlex", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-flex", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    public string WebkitBoxFlex
    {
        get => GetPropertyValue("-webkit-box-flex");
        set => SetProperty("-webkit-box-flex", value ?? "");
    }

    /// <summary>Gets or sets the <c>-webkit-box-ordinal-group</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxOrdinalGroup", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxOrdinalGroup", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-ordinal-group", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    public string WebkitBoxOrdinalGroup
    {
        get => GetPropertyValue("-webkit-box-ordinal-group");
        set => SetProperty("-webkit-box-ordinal-group", value ?? "");
    }

    /// <summary>Gets or sets the <c>-webkit-box-pack</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("WebkitBoxPack", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("webkitBoxPack", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-webkit_cased_attribute")]
    [IdlMember("-webkit-box-pack", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    public string WebkitBoxPack
    {
        get => GetPropertyValue("-webkit-box-pack");
        set => SetProperty("-webkit-box-pack", value ?? "");
    }

    /// <summary>Gets or sets the <c>grid-lanes-direction</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("gridLanesDirection", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("grid-lanes-direction", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    [Experimental(ExperimentalApis.GridLanesOrientation, UrlFormat = ExperimentalApis.UrlFormat)]
    public string GridLanesDirection
    {
        get => GetPropertyValue("grid-lanes-direction");
        set => SetProperty("grid-lanes-direction", value ?? "");
    }

    /// <summary>Gets or sets the <c>grid-lanes</c> shorthand of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("gridLanes", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("grid-lanes", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    [Experimental(ExperimentalApis.GridLanesOrientation, UrlFormat = ExperimentalApis.UrlFormat)]
    public string GridLanes
    {
        get => GetPropertyValue("grid-lanes");
        set => SetProperty("grid-lanes", value ?? "");
    }

    /// <summary>Gets or sets the <c>grid-lanes-pack</c> property of the declaration block.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("gridLanesPack", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("grid-lanes-pack", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    [Experimental(ExperimentalApis.GridLanesOrientation, UrlFormat = ExperimentalApis.UrlFormat)]
    public string GridLanesPack
    {
        get => GetPropertyValue("grid-lanes-pack");
        set => SetProperty("grid-lanes-pack", value ?? "");
    }

    /// <summary>Gets or sets the <c>flow-tolerance</c> property of the declaration block, the legacy name alias of <c>fit-tolerance</c>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute"/>
    [IdlMember("flowTolerance", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-camel_cased_attribute")]
    [IdlMember("flow-tolerance", "https://drafts.csswg.org/cssom/#dom-cssstyleproperties-dashed_attribute")]
    [Experimental(ExperimentalApis.NonStandardCssProperties, UrlFormat = ExperimentalApis.UrlFormat)]
    public string FlowTolerance
    {
        get => GetPropertyValue("flow-tolerance");
        set => SetProperty("flow-tolerance", value ?? "");
    }
}

/// <summary>https://drafts.csswg.org/css-fonts-5/#cssfontfacedescriptors</summary>
/// <seealso href="https://drafts.csswg.org/css-fonts-5/#cssfontfacedescriptors"/>
[IdlInterface("CSSFontFaceDescriptors", "https://drafts.csswg.org/css-fonts-5/#cssfontfacedescriptors")]
public sealed class CssFontFaceDescriptors : CssStyleDeclaration
{
    internal CssFontFaceDescriptors(CssRule parentRule, List<CssDeclarationEntry>? declarations)
        : base(parentRule, declarations, "@font-face")
    {
    }

    /// <summary>Gets or sets the <c>src</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-src"/>
    [IdlMember("src", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-src")]
    public string Src
    {
        get => GetPropertyValue("src");
        set => SetProperty("src", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-family</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontfamily"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-family"/>
    [IdlMember("fontFamily", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontfamily")]
    [IdlMember("font-family", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-family")]
    public string FontFamily
    {
        get => GetPropertyValue("font-family");
        set => SetProperty("font-family", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-style</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontstyle"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-style"/>
    [IdlMember("fontStyle", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontstyle")]
    [IdlMember("font-style", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-style")]
    public string FontStyle
    {
        get => GetPropertyValue("font-style");
        set => SetProperty("font-style", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-weight</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontweight"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-weight"/>
    [IdlMember("fontWeight", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontweight")]
    [IdlMember("font-weight", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-weight")]
    public string FontWeight
    {
        get => GetPropertyValue("font-weight");
        set => SetProperty("font-weight", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-stretch</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontstretch"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-stretch"/>
    [IdlMember("fontStretch", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontstretch")]
    [IdlMember("font-stretch", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-stretch")]
    public string FontStretch
    {
        get => GetPropertyValue("font-stretch");
        set => SetProperty("font-stretch", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-width</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontwidth"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-width"/>
    [IdlMember("fontWidth", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontwidth")]
    [IdlMember("font-width", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-width")]
    public string FontWidth
    {
        get => GetPropertyValue("font-width");
        set => SetProperty("font-width", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-size</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontsize"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-size"/>
    [IdlMember("fontSize", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontsize")]
    [IdlMember("font-size", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-size")]
    public string FontSize
    {
        get => GetPropertyValue("font-size");
        set => SetProperty("font-size", value ?? "");
    }

    /// <summary>Gets or sets the <c>size-adjust</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-sizeadjust"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-size-adjust"/>
    [IdlMember("sizeAdjust", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-sizeadjust")]
    [IdlMember("size-adjust", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-size-adjust")]
    public string SizeAdjust
    {
        get => GetPropertyValue("size-adjust");
        set => SetProperty("size-adjust", value ?? "");
    }

    /// <summary>Gets or sets the <c>unicode-range</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-unicoderange"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-unicode-range"/>
    [IdlMember("unicodeRange", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-unicoderange")]
    [IdlMember("unicode-range", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-unicode-range")]
    public string UnicodeRange
    {
        get => GetPropertyValue("unicode-range");
        set => SetProperty("unicode-range", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-feature-settings</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontfeaturesettings"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-feature-settings"/>
    [IdlMember("fontFeatureSettings", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontfeaturesettings")]
    [IdlMember("font-feature-settings", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-feature-settings")]
    public string FontFeatureSettings
    {
        get => GetPropertyValue("font-feature-settings");
        set => SetProperty("font-feature-settings", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-variation-settings</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontvariationsettings"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-variation-settings"/>
    [IdlMember("fontVariationSettings", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontvariationsettings")]
    [IdlMember("font-variation-settings", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-variation-settings")]
    public string FontVariationSettings
    {
        get => GetPropertyValue("font-variation-settings");
        set => SetProperty("font-variation-settings", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-named-instance</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontnamedinstance"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-named-instance"/>
    [IdlMember("fontNamedInstance", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontnamedinstance")]
    [IdlMember("font-named-instance", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-named-instance")]
    public string FontNamedInstance
    {
        get => GetPropertyValue("font-named-instance");
        set => SetProperty("font-named-instance", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-display</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontdisplay"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-display"/>
    [IdlMember("fontDisplay", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontdisplay")]
    [IdlMember("font-display", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-display")]
    public string FontDisplay
    {
        get => GetPropertyValue("font-display");
        set => SetProperty("font-display", value ?? "");
    }

    /// <summary>Gets or sets the <c>font-language-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontlanguageoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-language-override"/>
    [IdlMember("fontLanguageOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-fontlanguageoverride")]
    [IdlMember("font-language-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-font-language-override")]
    public string FontLanguageOverride
    {
        get => GetPropertyValue("font-language-override");
        set => SetProperty("font-language-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>ascent-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-ascentoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-ascent-override"/>
    [IdlMember("ascentOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-ascentoverride")]
    [IdlMember("ascent-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-ascent-override")]
    public string AscentOverride
    {
        get => GetPropertyValue("ascent-override");
        set => SetProperty("ascent-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>descent-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-descentoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-descent-override"/>
    [IdlMember("descentOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-descentoverride")]
    [IdlMember("descent-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-descent-override")]
    public string DescentOverride
    {
        get => GetPropertyValue("descent-override");
        set => SetProperty("descent-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>line-gap-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-linegapoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-line-gap-override"/>
    [IdlMember("lineGapOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-linegapoverride")]
    [IdlMember("line-gap-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-line-gap-override")]
    public string LineGapOverride
    {
        get => GetPropertyValue("line-gap-override");
        set => SetProperty("line-gap-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>superscript-position-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscriptpositionoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscript-position-override"/>
    [IdlMember("superscriptPositionOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscriptpositionoverride")]
    [IdlMember("superscript-position-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscript-position-override")]
    public string SuperscriptPositionOverride
    {
        get => GetPropertyValue("superscript-position-override");
        set => SetProperty("superscript-position-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>subscript-position-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscriptpositionoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscript-position-override"/>
    [IdlMember("subscriptPositionOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscriptpositionoverride")]
    [IdlMember("subscript-position-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscript-position-override")]
    public string SubscriptPositionOverride
    {
        get => GetPropertyValue("subscript-position-override");
        set => SetProperty("subscript-position-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>superscript-size-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscriptsizeoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscript-size-override"/>
    [IdlMember("superscriptSizeOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscriptsizeoverride")]
    [IdlMember("superscript-size-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-superscript-size-override")]
    public string SuperscriptSizeOverride
    {
        get => GetPropertyValue("superscript-size-override");
        set => SetProperty("superscript-size-override", value ?? "");
    }

    /// <summary>Gets or sets the <c>subscript-size-override</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscriptsizeoverride"/>
    /// <seealso href="https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscript-size-override"/>
    [IdlMember("subscriptSizeOverride", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscriptsizeoverride")]
    [IdlMember("subscript-size-override", "https://drafts.csswg.org/css-fonts-5/#dom-cssfontfacedescriptors-subscript-size-override")]
    public string SubscriptSizeOverride
    {
        get => GetPropertyValue("subscript-size-override");
        set => SetProperty("subscript-size-override", value ?? "");
    }
}

/// <summary>https://drafts.csswg.org/cssom/#csspagedescriptors</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#csspagedescriptors"/>
[IdlInterface("CSSPageDescriptors", "https://drafts.csswg.org/cssom/#csspagedescriptors")]
public sealed class CssPageDescriptors : CssStyleDeclaration
{
    internal CssPageDescriptors(CssRule parentRule, List<CssDeclarationEntry>? declarations)
        : base(parentRule, declarations, "@page")
    {
    }

    /// <summary>Gets or sets the <c>margin</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin"/>
    [IdlMember("margin", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin")]
    public string Margin
    {
        get => GetPropertyValue("margin");
        set => SetProperty("margin", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-top</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margintop"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-top"/>
    [IdlMember("marginTop", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margintop")]
    [IdlMember("margin-top", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-top")]
    public string MarginTop
    {
        get => GetPropertyValue("margin-top");
        set => SetProperty("margin-top", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-right</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginright"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-right"/>
    [IdlMember("marginRight", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginright")]
    [IdlMember("margin-right", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-right")]
    public string MarginRight
    {
        get => GetPropertyValue("margin-right");
        set => SetProperty("margin-right", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-bottom</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginbottom"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-bottom"/>
    [IdlMember("marginBottom", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginbottom")]
    [IdlMember("margin-bottom", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-bottom")]
    public string MarginBottom
    {
        get => GetPropertyValue("margin-bottom");
        set => SetProperty("margin-bottom", value ?? "");
    }

    /// <summary>Gets or sets the <c>margin-left</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginleft"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-left"/>
    [IdlMember("marginLeft", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marginleft")]
    [IdlMember("margin-left", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-margin-left")]
    public string MarginLeft
    {
        get => GetPropertyValue("margin-left");
        set => SetProperty("margin-left", value ?? "");
    }

    /// <summary>Gets or sets the <c>size</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-size"/>
    [IdlMember("size", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-size")]
    public string Size
    {
        get => GetPropertyValue("size");
        set => SetProperty("size", value ?? "");
    }

    /// <summary>Gets or sets the <c>page-orientation</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-pageorientation"/>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-page-orientation"/>
    [IdlMember("pageOrientation", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-pageorientation")]
    [IdlMember("page-orientation", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-page-orientation")]
    public string PageOrientation
    {
        get => GetPropertyValue("page-orientation");
        set => SetProperty("page-orientation", value ?? "");
    }

    /// <summary>Gets or sets the <c>marks</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marks"/>
    [IdlMember("marks", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-marks")]
    public string Marks
    {
        get => GetPropertyValue("marks");
        set => SetProperty("marks", value ?? "");
    }

    /// <summary>Gets or sets the <c>bleed</c> descriptor of the rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-csspagedescriptors-bleed"/>
    [IdlMember("bleed", "https://drafts.csswg.org/cssom/#dom-csspagedescriptors-bleed")]
    public string Bleed
    {
        get => GetPropertyValue("bleed");
        set => SetProperty("bleed", value ?? "");
    }
}

/// <summary>Parses the &lt;urange&gt; values (https://drafts.csswg.org/css-syntax-3/#urange-syntax).</summary>
internal static class CssUnicodeRanges
{
    /// <summary>Parses a comma-separated list of &lt;urange&gt; and returns its serialization, or null when it is invalid.</summary>
    public static string? Parse(List<CssComponentValue> values, string source)
    {
        var result = new List<string>();
        var current = new List<CssComponentValue>();
        foreach (var value in values.Append(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma })))
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Comma })
            {
                current.Add(value);
                continue;
            }

            // Whitespace is only allowed around the ranges
            var start = current.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
            var end = current.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
            if (start < 0)
                return null;

            var range = ParseRange(current[start..(end + 1)], source);
            if (range is null)
                return null;

            result.Add(range);
            current = [];
        }

        return string.Join(", ", result);
    }

    private static string? ParseRange(List<CssComponentValue> tokens, string source)
    {
        if (tokens.Count < 2 || tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident } u || !u.Token.IsIdent("u"))
            return null;

        // The representations of the tokens after "u", concatenated
        var text = new StringBuilder();
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i] is not CssPreservedToken { Token: var token } preserved)
                return null;

            if (!(token.Type is CssTokenType.Ident or CssTokenType.Number or CssTokenType.Dimension || token.IsDelim('+') || token.IsDelim('?')))
                return null;

            if (preserved.Start < 0 || preserved.End > source.Length || preserved.Start >= preserved.End)
                return null;

            text.Append(source, preserved.Start, preserved.End - preserved.Start);
        }

        var value = text.ToString();
        var index = 0;
        if (index >= value.Length || value[index] != '+')
            return null;

        index++;
        var digitsStart = index;
        while (index < value.Length && char.IsAsciiHexDigit(value[index]) && index - digitsStart < 6)
        {
            index++;
        }

        var hexEnd = index;
        while (index < value.Length && value[index] == '?' && index - digitsStart < 6)
        {
            index++;
        }

        var consumed = index - digitsStart;
        if (consumed == 0)
            return null;

        int rangeStart;
        int rangeEnd;
        if (index > hexEnd)
        {
            // Wildcards: the range covers all the values of the ? digits
            if (index != value.Length)
                return null;

            var prefix = value[digitsStart..hexEnd];
            var wildcards = index - hexEnd;
            rangeStart = Convert.ToInt32(prefix + new string('0', wildcards), 16);
            rangeEnd = Convert.ToInt32(prefix + new string('F', wildcards), 16);
        }
        else
        {
            rangeStart = Convert.ToInt32(value[digitsStart..hexEnd], 16);
            if (index == value.Length)
            {
                rangeEnd = rangeStart;
            }
            else
            {
                if (value[index] != '-')
                    return null;

                index++;
                var endStart = index;
                while (index < value.Length && char.IsAsciiHexDigit(value[index]) && index - endStart < 6)
                {
                    index++;
                }

                if (index == endStart || index != value.Length)
                    return null;

                rangeEnd = Convert.ToInt32(value[endStart..index], 16);
            }
        }

        if (rangeEnd > 0x10FFFF || rangeStart > rangeEnd)
            return null;

        var serialized = "U+" + rangeStart.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        return rangeEnd == rangeStart ? serialized : serialized + "-" + rangeEnd.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
    }
}
