using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The named properties of the Document interface (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem)
public partial class Document : ISupportedPropertyNames
{
    /// <summary>
    /// Returns the value of a named property of the document (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem):
    /// the embed, form, iframe, img and object elements of the document tree whose name attribute is <paramref name="name"/>,
    /// the object elements whose id is <paramref name="name"/>, and the img elements whose id is <paramref name="name"/>
    /// and that have a non-empty name attribute. The value is the element when there is a single one (the
    /// <see cref="Window"/> of its content navigable for an iframe element), an <see cref="HtmlCollection"/> of the elements
    /// when there are several, and null when there is none.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem"/>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem")]
    public object? this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!HasNamedItemCandidates || name.Length == 0)
                return null;

            var map = DocumentNamedItemMap.GetOrCreate(this);
            return map.GetCount(name, out var single) switch
            {
                0 => null,
                1 => single is HtmlIFrameElement { ContentWindow: { } window } ? window : single,
                _ => map.GetCollection(this, name),
            };
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-namedItem-which</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        if (!HasNamedItemCandidates)
            return [];

        // In tree order, the id before the name of an element, ignoring later duplicates
        var names = new List<string>();
        HashSet<string>? seen = null;
        for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is not Element element || !DocumentNamedItemMap.IsCandidate(element))
                continue;

            DocumentNamedItemMap.GetNames(element, element.IdValue, element.GetAttributeValue("name"), out var idName, out var nameName);
            if (idName is not null && (seen ??= new HashSet<string>(StringComparer.Ordinal)).Add(idName))
            {
                names.Add(idName);
            }

            if (nameName is not null && (seen ??= new HashSet<string>(StringComparer.Ordinal)).Add(nameName))
            {
                names.Add(nameName);
            }
        }

        return names;
    }

    // The bindings check whether a name is a named property on each property access of the document (document.body...):
    // a document without candidate elements answers without a lookup, the others with a lookup in the map of the names
    bool ISupportedPropertyNames.IsSupportedPropertyName(string name)
        => HasNamedItemCandidates && name.Length != 0 && DocumentNamedItemMap.GetOrCreate(this).Contains(name);
}
