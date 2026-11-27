using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The custom data attributes of an element (https://html.spec.whatwg.org/multipage/dom.html#domstringmap).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#domstringmap"/>
[IdlInterface("DOMStringMap", "https://html.spec.whatwg.org/multipage/dom.html#domstringmap")]
public sealed class DomStringMap : IEnumerable<KeyValuePair<string, string>>, ISupportedPropertyNames, Scripting.IScriptWrapperHolder
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
            var index = FindAttribute(name);
            return index < 0 ? null : _element.GetAttributeValue(index);
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

        return FindAttribute(name) >= 0;
    }

    /// <summary>
    /// Returns the index of the attribute that has no namespace and whose local name is <see cref="ToAttributeName"/> of the
    /// name, or -1 (the name is compared without creating the attribute name).
    /// </summary>
    private int FindAttribute(string name)
    {
        for (var i = 0; i < _element.AttributeCount; i++)
        {
            if (_element.GetAttributeNamespace(i) is null && IsAttributeName(_element.GetAttributeLocalName(i), name))
                return i;
        }

        return -1;
    }

    /// <summary>Returns whether the local name is <see cref="ToAttributeName"/> of the name.</summary>
    private static bool IsAttributeName(string localName, string name)
    {
        if (!localName.StartsWith("data-", StringComparison.Ordinal))
            return false;

        var position = 5;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                if (position + 1 >= localName.Length || localName[position] != '-' || localName[position + 1] != (char)(c + 0x20))
                    return false;

                position += 2;
            }
            else
            {
                if (position >= localName.Length || localName[position] != c)
                    return false;

                position++;
            }
        }

        return position == localName.Length;
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
    Scripting.ScriptWrapperSlot? Scripting.IScriptWrapperHolder.ScriptWrapper { get; set; }

    /// <inheritdoc/>
    Window? Scripting.IScriptWrapperHolder.ScriptWrapperWindow => _element.NodeDocument.RealmWindow;

    /// <inheritdoc/>
    [NotInIdl("Enumerates the name-value pairs")]
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => GetPairs().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
