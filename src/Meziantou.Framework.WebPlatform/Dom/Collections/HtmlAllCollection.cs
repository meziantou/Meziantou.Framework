using System.Collections;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The <c>(HTMLCollection or Element)</c> union returned by the named getters of <see cref="HtmlAllCollection"/>.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlallcollection"/>
[IdlInterface("(HTMLCollection or Element)", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlallcollection")]
public readonly struct HtmlCollectionOrElement : IUnionValue, IEquatable<HtmlCollectionOrElement>
{
    private HtmlCollectionOrElement(object value) => Value = value;

    /// <summary>Gets the value: an <c>HtmlCollection</c> or an <c>Element</c>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a collection, or null when it is an element.</summary>
    [NotInIdl]
    public HtmlCollection? HtmlCollection => Value as HtmlCollection;

    /// <summary>Gets the value as an element, or null when it is a collection.</summary>
    [NotInIdl]
    public Element? Element => Value as Element;

    /// <summary>Converts a <c>HtmlCollection</c> to a <c>HtmlCollectionOrElement</c>.</summary>
    [NotInIdl]
    public static implicit operator HtmlCollectionOrElement(HtmlCollection value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>Element</c> to a <c>HtmlCollectionOrElement</c>.</summary>
    [NotInIdl]
    public static implicit operator HtmlCollectionOrElement(Element value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>HtmlCollectionOrElement</c> from a <c>HtmlCollection</c>.</summary>
    [NotInIdl]
    public static HtmlCollectionOrElement FromHtmlCollection(HtmlCollection value) => value;

    /// <summary>Creates a <c>HtmlCollectionOrElement</c> from a <c>Element</c>.</summary>
    [NotInIdl]
    public static HtmlCollectionOrElement FromElement(Element value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(HtmlCollectionOrElement other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is HtmlCollectionOrElement other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>HtmlCollectionOrElement</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(HtmlCollectionOrElement left, HtmlCollectionOrElement right) => left.Equals(right);

    /// <summary>Determines whether two <c>HtmlCollectionOrElement</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(HtmlCollectionOrElement left, HtmlCollectionOrElement right) => !left.Equals(right);
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#the-htmlallcollection-interface. The
/// <c>[[IsHTMLDDA]]</c> and <c>[[Call]]</c> behaviors only exist in JavaScript.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlallcollection"/>
[IdlInterface("HTMLAllCollection", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlallcollection")]
public sealed class HtmlAllCollection : IReadOnlyList<Element>
{
    private readonly HtmlCollection _elements;

    internal HtmlAllCollection(Document document)
    {
        Document = document;
        _elements = new HtmlCollection(document, static _ => true);
    }

    private Document Document { get; }

    /// <summary>Gets the number of elements in the collection.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-length")]
    public uint Length => _elements.Length;

    /// <summary>Gets the number of elements in the collection.</summary>
    [NotInIdl]
    public int Count => _elements.Count;

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item"/>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Element? this[uint index] => _elements.Item(index);

    /// <summary>Gets the element at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Element this[int index] => _elements[index];

    /// <summary>
    /// Gets the element or the collection of elements with the given <c>id</c> or <c>name</c>, or null when there is
    /// none.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public HtmlCollectionOrElement? this[string name] => NamedItem(name);

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-nameditem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-nameditem")]
    public HtmlCollectionOrElement? NamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#concept-get-all-named
        if (name.Length == 0)
            return null;

        Element? first = null;
        var count = 0;
        foreach (var element in _elements)
        {
            if (IsNamed(element, name))
            {
                first ??= element;
                count++;
                if (count > 1)
                    break;
            }
        }

        return count switch
        {
            0 => null,
            1 => first!,
            _ => new HtmlCollection(Document, element => IsNamed(element, name)),
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlallcollection-item")]
    public HtmlCollectionOrElement? Item(string? nameOrIndex = null)
    {
        if (nameOrIndex is null)
            return null;

        if (TryParseArrayIndex(nameOrIndex, out var index))
        {
            var element = _elements.Item(index);
            return element is null ? null : (HtmlCollectionOrElement?)element;
        }

        return NamedItem(nameOrIndex);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlallcollection-0</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        var result = new List<string>();
        foreach (var element in _elements)
        {
            if (element.IdValue is { Length: > 0 } id && !result.Contains(id))
            {
                result.Add(id);
            }

            if (IsAllNamedElement(element) && element.GetAttributeValue("name") is { Length: > 0 } name && !result.Contains(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<Element> GetEnumerator() => _elements.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool IsNamed(Element element, string name) =>
        element.IdValue == name || (IsAllNamedElement(element) && element.GetAttributeValue("name") == name);

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#all-named-elements</summary>
    private static bool IsAllNamedElement(Element element) =>
        element.IsHtmlNamespace && element.TagId is TagId.A or TagId.Button or TagId.Embed or TagId.Form or TagId.Frame or TagId.Frameset
            or TagId.Iframe or TagId.Img or TagId.Input or TagId.Map or TagId.Meta or TagId.Object or TagId.Select or TagId.Textarea;

    /// <summary>https://tc39.es/ecma262/#array-index</summary>
    private static bool TryParseArrayIndex(string value, out uint index)
    {
        index = 0;
        if (value.Length is 0 or > 10 || (value.Length > 1 && value[0] == '0'))
            return false;

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return uint.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index) && index != uint.MaxValue;
    }
}
