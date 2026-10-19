using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The custom data attributes of an element (https://html.spec.whatwg.org/multipage/dom.html#domstringmap).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#domstringmap"/>
[IdlInterface("DOMStringMap", "https://html.spec.whatwg.org/multipage/dom.html#domstringmap")]
public sealed class DomStringMap : IEnumerable<KeyValuePair<string, string>>, ISupportedPropertyNames
{
    private readonly Element _element;

    internal DomStringMap(Element element)
    {
        _element = element;
    }

    /// <summary>Gets or sets the value of the data-* attribute corresponding to the name (e.g. "fooBar" for data-foo-bar).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-nameditem"/>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-setitem"/>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-nameditem")]
    [IdlMember("setter", "https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-setitem")]
    public string? this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return _element.GetAttributeValue(ToAttributeName(name));
        }
        set
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(value);

            // https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-setitem
            for (var i = 0; i + 1 < name.Length; i++)
            {
                if (name[i] == '-' && char.IsAsciiLetterLower(name[i + 1]))
                    throw DomException.Syntax($"'{name}' is not a valid name: a '-' is followed by a lowercase letter");
            }

            var attributeName = ToAttributeName(name);
            if (!XmlNames.IsValidAttributeLocalName(attributeName))
                throw DomException.InvalidCharacter($"'{attributeName}' is not a valid attribute name");

            _element.SetAttributeValue(attributeName, value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-removeitem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-removeitem"/>
    [IdlMember("deleter", "https://html.spec.whatwg.org/multipage/dom.html#dom-domstringmap-removeitem")]
    public void Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _element.RemoveAttributeByLocalName(ToAttributeName(name));
    }

    /// <summary>The names of the name-value pairs (https://html.spec.whatwg.org/multipage/dom.html#concept-domstringmap-pairs).</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        var result = new List<string>();
        foreach (var (name, _) in GetPairs())
        {
            result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// Returns whether a name is the name of a name-value pair: the name of a pair never contains a '-' followed by an ASCII
    /// lower alpha, and the other names are the names of the data-* attributes that <see cref="ToAttributeName"/> returns.
    /// </summary>
    bool ISupportedPropertyNames.IsSupportedPropertyName(string name)
    {
        for (var i = 0; i + 1 < name.Length; i++)
        {
            if (name[i] == '-' && char.IsAsciiLetterLower(name[i + 1]))
                return false;
        }

        return _element.GetAttributeNS(@namespace: null, ToAttributeName(name)) is not null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#concept-domstringmap-pairs</summary>
    private List<KeyValuePair<string, string>> GetPairs()
    {
        var result = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < _element.AttributeCount; i++)
        {
            if (_element.GetAttributeNamespace(i) is not null)
                continue;

            var localName = _element.GetAttributeLocalName(i);
            if (!localName.StartsWith("data-", StringComparison.Ordinal) || localName.AsSpan().ContainsAnyInRange('A', 'Z'))
                continue;

            var name = new StringBuilder(localName.Length - 5);
            for (var j = 5; j < localName.Length; j++)
            {
                if (localName[j] == '-' && j + 1 < localName.Length && char.IsAsciiLetterLower(localName[j + 1]))
                {
                    name.Append(char.ToUpperInvariant(localName[j + 1]));
                    j++;
                }
                else
                {
                    name.Append(localName[j]);
                }
            }

            result.Add(new KeyValuePair<string, string>(name.ToString(), _element.GetAttributeValue(i)));
        }

        return result;
    }

    // Inserts a '-' before each ASCII upper alpha, lowercases it, and prepends "data-"
    private static string ToAttributeName(string name)
    {
        if (!name.AsSpan().ContainsAnyInRange('A', 'Z'))
            return string.Concat("data-", name);

        var result = new StringBuilder("data-", name.Length + 8);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                result.Append('-').Append((char)(c + 0x20));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <inheritdoc/>
    [NotInIdl("Enumerates the name-value pairs")]
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => GetPairs().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
