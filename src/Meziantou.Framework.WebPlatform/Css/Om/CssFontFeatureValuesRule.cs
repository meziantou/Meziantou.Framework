using System.Collections;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The type of a feature value block of a @font-feature-values rule (https://drafts.csswg.org/css-fonts-4/#feature-value-blocks).</summary>
internal enum CssFontFeatureType
{
    Stylistic,
    HistoricalForms,
    Styleset,
    CharacterVariant,
    Swash,
    Ornaments,
    Annotation,
}

/// <summary>
/// https://drafts.csswg.org/css-fonts-4/#font-feature-values: the names of the feature values of the <c>font-variant-alternates</c>
/// functions of the families of the rule. The feature value blocks of the same type are merged (the last declaration of a
/// name wins). The feature values are not used: the fonts are not rendered.
/// </summary>
[IdlInterface("CSSFontFeatureValuesRule", "https://drafts.csswg.org/css-fonts-4/#cssfontfeaturevaluesrule")]
public sealed class CssFontFeatureValuesRule : CssRule
{
    /// <summary>The at-keywords of the feature value blocks, in the order of <see cref="CssFontFeatureType"/>.</summary>
    private static readonly string[] BlockNames = ["stylistic", "historical-forms", "styleset", "character-variant", "swash", "ornaments", "annotation"];

    private readonly CssFontFeatureValuesMap[] _maps;
    private readonly List<CssFontFeatureType> _blockOrder = [];
    private readonly List<CssDeclarationEntry> _descriptors;
    private List<string> _families;

    internal CssFontFeatureValuesRule(List<string> families, List<CssDeclarationEntry> descriptors)
    {
        _families = families;
        _descriptors = descriptors;
        _maps = [.. Enum.GetValues<CssFontFeatureType>().Select(type => new CssFontFeatureValuesMap(type))];
    }

    /// <summary>Gets the type of the rule: <see cref="CssRule.FontFeatureValuesRule"/>.</summary>
    public override ushort Type => FontFeatureValuesRule;

    /// <summary>The family names of the rule, serialized as a comma-separated list. Setting a value that is not a list of family names does nothing.</summary>
    [IdlMember("fontFamily", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-fontfamily")]
    public string FontFamily
    {
        get => string.Join(", ", _families.Select(FontFace.SerializeFamilyName));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ParseFamilyNames(CssParser.ParseListOfComponentValues(value)) is { } families)
            {
                _families = families;
            }
        }
    }

    /// <summary>Gets the map of the <c>@annotation</c> feature values.</summary>
    [IdlMember("annotation", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-annotation")]
    public CssFontFeatureValuesMap Annotation => _maps[(int)CssFontFeatureType.Annotation];

    /// <summary>Gets the map of the <c>@ornaments</c> feature values.</summary>
    [IdlMember("ornaments", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-ornaments")]
    public CssFontFeatureValuesMap Ornaments => _maps[(int)CssFontFeatureType.Ornaments];

    /// <summary>Gets the map of the <c>@stylistic</c> feature values.</summary>
    [IdlMember("stylistic", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-stylistic")]
    public CssFontFeatureValuesMap Stylistic => _maps[(int)CssFontFeatureType.Stylistic];

    /// <summary>Gets the map of the <c>@swash</c> feature values.</summary>
    [IdlMember("swash", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-swash")]
    public CssFontFeatureValuesMap Swash => _maps[(int)CssFontFeatureType.Swash];

    /// <summary>Gets the map of the <c>@character-variant</c> feature values.</summary>
    [IdlMember("characterVariant", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-charactervariant")]
    public CssFontFeatureValuesMap CharacterVariant => _maps[(int)CssFontFeatureType.CharacterVariant];

    /// <summary>Gets the map of the <c>@styleset</c> feature values.</summary>
    [IdlMember("styleset", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-styleset")]
    public CssFontFeatureValuesMap Styleset => _maps[(int)CssFontFeatureType.Styleset];

    /// <summary>Gets the map of the <c>@historical-forms</c> feature values.</summary>
    [IdlMember("historicalForms", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesrule-historicalforms")]
    public CssFontFeatureValuesMap HistoricalForms => _maps[(int)CssFontFeatureType.HistoricalForms];

    /// <summary>The feature value blocks are written on their own line.</summary>
    internal override bool HasMultilineBlock => true;

    internal override string GetPrelude() => "@font-feature-values " + FontFamily;

    internal override List<CssSerializedDeclaration> GetDeclarations() => CssStyleDeclaration.GetSerializedDeclarations(_descriptors);

    /// <summary>
    /// The non-empty feature value blocks, serialized as at-rules whose declarations are the feature values: in the order of
    /// their first block in the style sheet (as Firefox and Safari), then the other types.
    /// </summary>
    internal override List<CssRule>? ChildRules => [.. _blockOrder.Concat(Enum.GetValues<CssFontFeatureType>().Except(_blockOrder)).Select(type => _maps[(int)type]).Where(map => map.Count > 0).Select(map => new FeatureValueBlock(map))];

    /// <summary>Returns the feature type of the at-keyword of a feature value block, or null.</summary>
    internal static CssFontFeatureType? GetFeatureType(string name)
    {
        var index = Array.IndexOf(BlockNames, InfraAscii.ToAsciiLowercase(name));
        return index < 0 ? null : (CssFontFeatureType)index;
    }

    internal CssFontFeatureValuesMap GetMap(CssFontFeatureType type) => _maps[(int)type];

    /// <summary>Returns the map of a feature value block of the style sheet, and records the order of the blocks for the serialization.</summary>
    internal CssFontFeatureValuesMap GetMapForBlock(CssFontFeatureType type)
    {
        if (!_blockOrder.Contains(type))
        {
            _blockOrder.Add(type);
        }

        return _maps[(int)type];
    }

    /// <summary>The family names of the rule.</summary>
    internal IReadOnlyList<string> Families => _families;

    /// <summary>
    /// Parses a comma-separated list of <c>&lt;family-name&gt;</c> (a string, or a sequence of identifiers that are not CSS-wide
    /// keywords, <c>default</c> or a single generic family), or returns null when the list is invalid.
    /// </summary>
    internal static List<string>? ParseFamilyNames(List<CssComponentValue> values)
    {
        var families = new List<string>();
        foreach (var entry in Cascade.CssValueComputer.SplitArguments(values))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is [CssPreservedToken { Type: CssTokenType.String } stringToken])
            {
                families.Add(stringToken.Token.Value);
                continue;
            }

            if (significant.Count == 0 || !significant.TrueForAll(component => component is CssPreservedToken { Type: CssTokenType.Ident } ident && !ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value)) && !InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "default")))
                return null;

            var name = string.Join(' ', significant.Select(component => ((CssPreservedToken)component).Token.Value));
            if (significant.Count == 1 && FontFace.IsGenericFamily(InfraAscii.ToAsciiLowercase(name)))
                return null;

            families.Add(name);
        }

        return families.Count > 0 ? families : null;
    }

    /// <summary>A feature value block, serialized as <c>@styleset { name: 1 2; }</c>.</summary>
    private sealed class FeatureValueBlock(CssFontFeatureValuesMap map) : CssRule
    {
        internal override string GetPrelude() => "@" + BlockNames[(int)map.FeatureType];

        internal override List<CssSerializedDeclaration> GetDeclarations() => [.. map.Select(entry => new CssSerializedDeclaration(CssSerializer.SerializeIdentifier(entry.Key), string.Join(' ', entry.Value.Select(value => value.ToString(CultureInfo.InvariantCulture))), Important: false))];
    }
}

/// <summary>
/// https://drafts.csswg.org/css-fonts-4/#cssfontfeaturevaluesmap: the feature values of a feature value block of a
/// @font-feature-values rule, by name, in insertion order.
/// </summary>
[IdlInterface("CSSFontFeatureValuesMap", "https://drafts.csswg.org/css-fonts-4/#cssfontfeaturevaluesmap")]
public sealed class CssFontFeatureValuesMap : IReadOnlyCollection<KeyValuePair<string, IReadOnlyList<uint>>>
{
    private readonly List<KeyValuePair<string, IReadOnlyList<uint>>> _entries = [];

    internal CssFontFeatureValuesMap(CssFontFeatureType featureType)
    {
        FeatureType = featureType;
    }

    internal CssFontFeatureType FeatureType { get; }

    /// <summary>Gets the number of feature values.</summary>
    [NotInIdl("The size member of maplike<CSSOMString, sequence<unsigned long>>")]
    public int Count => _entries.Count;

    /// <summary>Returns the values of the feature value name, or null.</summary>
    [NotInIdl("The get() member of maplike<CSSOMString, sequence<unsigned long>>")]
    public IReadOnlyList<uint>? Get(string featureValueName)
    {
        ArgumentNullException.ThrowIfNull(featureValueName);
        var index = IndexOf(featureValueName);
        return index < 0 ? null : _entries[index].Value;
    }

    /// <summary>Returns whether the feature value name is defined.</summary>
    [NotInIdl("The has() member of maplike<CSSOMString, sequence<unsigned long>>")]
    public bool Has(string featureValueName)
    {
        ArgumentNullException.ThrowIfNull(featureValueName);
        return IndexOf(featureValueName) >= 0;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesmap-set: defines the feature value name. Throws an
    /// <c>InvalidAccessError</c> when the number of values is not valid for the type of the feature value block.
    /// </summary>
    [IdlMember("set", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesmap-set")]
    public void Set(string featureValueName, uint values) => Set(featureValueName, [values]);

    /// <inheritdoc cref="Set(string, uint)"/>
    [IdlMember("set", "https://drafts.csswg.org/css-fonts-4/#dom-cssfontfeaturevaluesmap-set")]
    public void Set(string featureValueName, IReadOnlyList<uint> values)
    {
        ArgumentNullException.ThrowIfNull(featureValueName);
        ArgumentNullException.ThrowIfNull(values);
        if (!IsValidCount(FeatureType, values.Count))
            throw new DomException("The number of values is not valid for the feature type", "InvalidAccessError");

        SetValue(featureValueName, [.. values]);
    }

    /// <summary>Removes the feature value name. Returns whether it was defined.</summary>
    [NotInIdl("The delete() member of maplike<CSSOMString, sequence<unsigned long>>")]
    public bool Delete(string featureValueName)
    {
        ArgumentNullException.ThrowIfNull(featureValueName);
        var index = IndexOf(featureValueName);
        if (index < 0)
            return false;

        _entries.RemoveAt(index);
        return true;
    }

    /// <summary>Removes all the feature values.</summary>
    [NotInIdl("The clear() member of maplike<CSSOMString, sequence<unsigned long>>")]
    public void Clear() => _entries.Clear();

    /// <summary>Enumerates the feature value names and their values, in insertion order.</summary>
    [NotInIdl("The iterator of maplike<CSSOMString, sequence<unsigned long>>")]
    public IEnumerator<KeyValuePair<string, IReadOnlyList<uint>>> GetEnumerator()
    {
        // The entries added or removed during the enumeration are not visited
        foreach (var entry in _entries.ToArray())
        {
            yield return entry;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Whether a feature value of the type can have this number of values (https://drafts.csswg.org/css-fonts-4/#multi-value-features):
    /// one value, except <c>@styleset</c> (any number) and <c>@character-variant</c> (one or two).
    /// </summary>
    internal static bool IsValidCount(CssFontFeatureType type, int count) => type switch
    {
        CssFontFeatureType.Styleset => count > 0,
        CssFontFeatureType.CharacterVariant => count is 1 or 2,
        _ => count == 1,
    };

    internal void SetValue(string featureValueName, uint[] values)
    {
        var index = IndexOf(featureValueName);
        if (index < 0)
        {
            _entries.Add(new(featureValueName, values));
        }
        else
        {
            _entries[index] = new(featureValueName, values);
        }
    }

    private int IndexOf(string featureValueName) => _entries.FindIndex(entry => entry.Key == featureValueName);
}
