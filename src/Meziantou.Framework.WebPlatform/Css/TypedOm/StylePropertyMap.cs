using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>The (CSSStyleValue or USVString) union of <see cref="StylePropertyMap.Set"/> and <see cref="StylePropertyMap.Append"/>.</summary>
public readonly struct CssStyleValueOrString : IUnionValue, IEquatable<CssStyleValueOrString>
{
    private CssStyleValueOrString(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="CssStyleValue"/> or a <see cref="string"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>CssStyleValue</c> to a <c>CssStyleValueOrString</c>.</summary>
    [NotInIdl]
    public static implicit operator CssStyleValueOrString(CssStyleValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>string</c> to a <c>CssStyleValueOrString</c>.</summary>
    [NotInIdl]
    public static implicit operator CssStyleValueOrString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssStyleValueOrString</c> from a <c>CssStyleValue</c>.</summary>
    [NotInIdl]
    public static CssStyleValueOrString FromCssStyleValue(CssStyleValue value) => value;

    /// <summary>Creates a <c>CssStyleValueOrString</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssStyleValueOrString FromString(string value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssStyleValueOrString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssStyleValueOrString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssStyleValueOrString</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssStyleValueOrString left, CssStyleValueOrString right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssStyleValueOrString</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssStyleValueOrString left, CssStyleValueOrString right) => !left.Equals(right);
}

/// <summary>
/// The values of the properties of a declaration block or of a computed style, as <see cref="CssStyleValue"/> objects
/// (https://drafts.css-houdini.org/css-typed-om-1/#stylepropertymapreadonly). The map is live: it reflects the current
/// declarations. A list-valued property (e.g. transition-duration) has one value per iteration.
/// </summary>
[IdlInterface("StylePropertyMapReadOnly", "https://drafts.css-houdini.org/css-typed-om-1/#stylepropertymapreadonly")]
public abstract class StylePropertyMapReadOnly : IEnumerable<KeyValuePair<string, IReadOnlyList<CssStyleValue>>>, IIndexedPairIterable<string, IReadOnlyList<CssStyleValue>>
{
    private protected StylePropertyMapReadOnly()
    {
    }

    /// <summary>Returns the first value of a property, or null when the map does not contain it (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-get).</summary>
    [IdlMember("get", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-get")]
    public CssStyleValue? Get(string property)
    {
        var values = GetValues(GetPropertyName(property));
        return values is { Count: > 0 } ? values[0] : null;
    }

    /// <summary>Returns the values of a property (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-getall).</summary>
    [IdlMember("getAll", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-getall")]
    public IReadOnlyList<CssStyleValue> GetAll(string property) => GetValues(GetPropertyName(property)) ?? [];

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-has</summary>
    [IdlMember("has", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-has")]
    public bool Has(string property) => GetValues(GetPropertyName(property)) is not null;

    /// <summary>The number of properties of the map (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-size).</summary>
    [IdlMember("size", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymapreadonly-size")]
    public uint Size => (uint)GetNames().Count();

    /// <summary>Enumerates the properties and their values (https://drafts.css-houdini.org/css-typed-om-1/#the-stylepropertymap).</summary>
    [NotInIdl]
    public IEnumerator<KeyValuePair<string, IReadOnlyList<CssStyleValue>>> GetEnumerator()
    {
        foreach (var name in GetNames().ToList())
        {
            if (GetValues(name) is { } values)
                yield return new KeyValuePair<string, IReadOnlyList<CssStyleValue>>(name, values);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns the pair at an index without reifying the values of the other properties (the iterators of the bindings read one pair at each step).</summary>
    bool IIndexedPairIterable<string, IReadOnlyList<CssStyleValue>>.TryGetPair(int index, out KeyValuePair<string, IReadOnlyList<CssStyleValue>> pair)
    {
        if (GetName(index) is { } name && GetValues(name) is { } values)
        {
            pair = new KeyValuePair<string, IReadOnlyList<CssStyleValue>>(name, values);
            return true;
        }

        pair = default;
        return false;
    }

    /// <summary>Returns the values of a property (a valid property name), or null when the map does not contain it.</summary>
    private protected abstract List<CssStyleValue>? GetValues(string property);

    /// <summary>Returns the names of the properties of the map, in order: the properties that have values (see <see cref="GetValues"/>).</summary>
    private protected abstract IEnumerable<string> GetNames();

    /// <summary>Returns the name at an index of <see cref="GetNames"/>, or null when the index is out of range.</summary>
    private protected virtual string? GetName(int index) => GetNames().ElementAtOrDefault(index);

    /// <summary>Returns the name of a valid CSS property (https://drafts.css-houdini.org/css-typed-om-1/#valid-css-property), or throws a TypeError.</summary>
    private protected static string GetPropertyName(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return CssStyleValueReifier.NormalizePropertyName(property) ?? throw new TypeErrorException($"'{property}' is not a valid CSS property");
    }
}

/// <summary>
/// The declarations of a style rule or of the style attribute of an element (https://drafts.css-houdini.org/css-typed-om-1/#declared-stylepropertymap-objects),
/// as <see cref="CssStyleValue"/> objects. Setting a value updates the declarations.
/// </summary>
[IdlInterface("StylePropertyMap", "https://drafts.css-houdini.org/css-typed-om-1/#stylepropertymap")]
public sealed class StylePropertyMap : StylePropertyMapReadOnly
{
    private readonly CssStyleDeclaration _declarations;

    internal StylePropertyMap(CssStyleDeclaration declarations)
    {
        _declarations = declarations;
    }

    /// <summary>Sets the value of a property (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-set).</summary>
    [IdlMember("set", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-set")]
    public void Set(string property, params ReadOnlySpan<CssStyleValueOrString> values)
    {
        var name = GetPropertyName(property);
        if (values.Length > 1 && !CssStyleValueReifier.IsListValued(name))
            throw new TypeErrorException($"'{name}' is not a list-valued property");

        CheckAssociatedProperties(name, values);
        if (values.Length > 1 && ContainsVariableReference(values))
            throw new TypeErrorException("A list-valued property cannot be set to several values when one of them contains a var() reference");

        _declarations.ReplaceProperty(name, CreateInternalRepresentations(name, values), isTypedRepresentation: AreTypedValues(values));
    }

    /// <summary>Appends values to a list-valued property (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-append).</summary>
    [IdlMember("append", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-append")]
    public void Append(string property, params ReadOnlySpan<CssStyleValueOrString> values)
    {
        var name = GetPropertyName(property);
        if (!CssStyleValueReifier.IsListValued(name))
            throw new TypeErrorException($"'{name}' is not a list-valued property");

        CheckAssociatedProperties(name, values);
        if (ContainsVariableReference(values))
            throw new TypeErrorException("A var() reference cannot be appended");

        // The current value cannot be extended when it contains a var() reference (possibly through a shorthand) or is a CSS-wide keyword
        var longhand = CssPropertyRegistry.Get(name)!.Name;
        if (_declarations.Declarations.Find(d => d.Name == longhand) is { Value: { Value.Length: 0, ShorthandName: not null } })
            throw new TypeErrorException("The value contains a var() reference");

        var current = _declarations.GetPropertyValue(name);
        var currentValues = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(current));
        if (ValueMatcher.ContainsSubstitutionFunction(currentValues))
            throw new TypeErrorException("The value contains a var() reference");

        if (currentValues is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(keyword.Token.Value)))
            throw new TypeErrorException("A CSS-wide keyword cannot be appended to");

        var text = CreateInternalRepresentations(name, values);
        if (text.Length == 0)
            return;

        _declarations.SetProperty(name, current.Length == 0 ? text : current + ", " + text);
    }

    /// <summary>Removes a property (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-delete); removing a shorthand removes its longhands.</summary>
    [IdlMember("delete", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-delete")]
    public void Delete(string property) => _declarations.RemoveProperty(GetPropertyName(property));

    /// <summary>Removes all the declarations (https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-clear).</summary>
    [IdlMember("clear", "https://drafts.css-houdini.org/css-typed-om-1/#dom-stylepropertymap-clear")]
    public void Clear() => _declarations.CssText = "";

    private protected override List<CssStyleValue>? GetValues(string property)
    {
        var value = _declarations.GetPropertyValue(property);
        if (value.Length == 0)
            return null;

        var typedValue = _declarations.GetTypedValue(property, out var isRepresentation);
        return CssStyleValueReifier.ReifySpecified(property, value, typedValue, isRepresentation);
    }

    private protected override IEnumerable<string> GetNames() => _declarations.Declarations.Select(d => d.Name).Where(name => _declarations.GetPropertyValue(name).Length > 0);

    private static void CheckAssociatedProperties(string property, ReadOnlySpan<CssStyleValueOrString> values)
    {
        foreach (var value in values)
        {
            if (value.Value is CssStyleValue { AssociatedProperty: { } associatedProperty } && associatedProperty != property)
                throw new TypeErrorException($"The value was reified for '{associatedProperty}', not for '{property}'");
        }
    }

    /// <summary>Returns whether a value is a <see cref="CssUnparsedValue"/> or a string that contains a var() reference.</summary>
    private static bool ContainsVariableReference(ReadOnlySpan<CssStyleValueOrString> values)
    {
        foreach (var value in values)
        {
            if (value.Value is CssUnparsedValue || (value.Value is string text && ValueMatcher.ContainsSubstitutionFunction(CssParser.ParseListOfComponentValues(text))))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether every value is a keyword, a numeric value or a transform value: the declaration keeps them as its
    /// internal representation (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation), so that
    /// they are reified as they were set.
    /// </summary>
    private static bool AreTypedValues(ReadOnlySpan<CssStyleValueOrString> values)
    {
        foreach (var value in values)
        {
            if (value.Value is not (CssKeywordValue or CssNumericValue or CssTransformValue))
                return false;
        }

        return values.Length > 0;
    }

    /// <summary>Returns the text of the values, separated by commas (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation).</summary>
    private static string CreateInternalRepresentations(string property, ReadOnlySpan<CssStyleValueOrString> values)
    {
        var texts = new List<string>(values.Length);
        foreach (var value in values)
        {
            texts.Add(CssStyleValueReifier.CreateInternalRepresentation(property, value));
        }

        return string.Join(", ", texts);
    }
}

/// <summary>
/// The computed values of an element (https://drafts.css-houdini.org/css-typed-om-1/#computed-stylepropertymapreadonly-objects):
/// the longhands in lexicographical order (the vendor-prefixed ones last), then the custom properties. The values are the
/// computed values, not the resolved values of getComputedStyle() (e.g. a width in percentage is a percentage). The map is
/// empty when the element is not rendered (e.g. not connected).
/// </summary>
internal sealed class ComputedStylePropertyMap : StylePropertyMapReadOnly
{
    private readonly Element _element;

    public ComputedStylePropertyMap(Element element)
    {
        _element = element;
    }

    private ComputedStyleDeclarations Declarations => ComputedStyleDeclarations.Create(_element, pseudoElement: null);

    private protected override List<CssStyleValue>? GetValues(string property)
    {
        var declarations = Declarations;
        if (declarations.GetStyle() is not { } style)
            return null;

        if (CssPropertyRegistry.IsCustomProperty(property))
            return style.GetCustomPropertyValue(property) is { } customValue ? ReifyCustomProperty(_element.NodeDocument, property, customValue) : null;

        if (CssPropertyRegistry.Get(property) is not { } definition)
            return null;

        if (definition.IsShorthand)
            return CssStyleValueReifier.Reify(property, (definition.Name == "background" ? SerializeBackground(declarations) : null) ?? declarations.GetPropertyValue(property), computed: true);

        // The anchor functions of an anchor-positioned box compute to lengths (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos)
        var computedStyle = style.TryGetAnchorPositionedStyle(definition.Name) ?? style;
        var value = computedStyle.GetComputedValue(definition.Name);

        // https://drafts.csswg.org/css-text-4/#letter-spacing-property: a computed letter-spacing of zero is normal, as in
        // getComputedStyle() (as Chrome and Safari)
        if (definition.Name == "letter-spacing" && value == "0px")
        {
            value = "normal";
        }

        return CssStyleValueReifier.Reify(property, value, computed: true);
    }

    /// <summary>
    /// Serializes the computed value of the background shorthand with the computed value of every longhand of every layer,
    /// as Blink and WebKit do (the serialization of getComputedStyle() omits the initial values): the color (in the final
    /// layer), the image, the repetition, the attachment, the position, the size, the origin and the clip box. Returns null
    /// when a longhand has no value.
    /// </summary>
    private static string? SerializeBackground(ComputedStyleDeclarations declarations)
    {
        ReadOnlySpan<string> names = ["background-image", "background-repeat", "background-attachment", "background-position", "background-size", "background-origin", "background-clip"];
        var longhands = new List<string>[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            longhands[i] = CssBackgrounds.SplitList(declarations.GetPropertyValue(names[i]));
            if (longhands[i].Count == 0)
                return null;
        }

        var color = declarations.GetPropertyValue("background-color");
        if (color.Length == 0)
            return null;

        var layers = new List<string>();
        var count = longhands[0].Count;
        for (var layer = 0; layer < count; layer++)
        {
            var components = new List<string>();
            if (layer == count - 1)
            {
                components.Add(color);
            }

            for (var i = 0; i < names.Length; i++)
            {
                // The lists of the longhands are repeated to the number of layers (https://drafts.csswg.org/css-backgrounds-3/#layering)
                var value = longhands[i][layer % longhands[i].Count];
                components.Add(names[i] == "background-size" ? "/ " + value : value);
            }

            layers.Add(string.Join(' ', components));
        }

        return string.Join(", ", layers);
    }

    /// <summary>
    /// Reifies the computed value of a custom property: a registered custom property is reified according to its syntax
    /// (https://drafts.css-houdini.org/css-properties-values-api/#css-style-value-reification; e.g. a
    /// <c>&lt;length&gt;</c> is a <see cref="CssUnitValue"/>), the other ones as a list of component values.
    /// </summary>
    internal static List<CssStyleValue> ReifyCustomProperty(Document document, string property, string value)
        => document.GetStyleEngine().RegisteredProperties.TryGetValue(property, out var registration) && !registration.Syntax.IsUniversal
            ? CssStyleValueReifier.ReifyWithSyntax(property, registration.Syntax, value)
            : CssStyleValueReifier.Reify(property, value, computed: true);

    private protected override IEnumerable<string> GetNames() => Declarations.Names;

    private protected override string? GetName(int index)
    {
        var declarations = Declarations;
        return index < declarations.Count ? declarations.GetName(index) : null;
    }
}
