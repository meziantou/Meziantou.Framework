using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// The sanitization of the nodes while they are parsed (setHTML(), setHTMLUnsafe() with a sanitizer), as in Blink. The
/// tree construction is unchanged, but the elements removed by the sanitizer are never inserted (nor are their
/// descendants), the children of the elements replaced by their children are inserted in the nearest kept ancestor of the
/// stack of open elements, and the attributes removed by the sanitizer are never set. So, the sanitizer prevents the
/// side effects of the parsing: the constructors of the removed custom elements do not run, and the declarative shadow
/// roots use the sanitized attributes of their template element. The WPT tests are
/// sanitizer-api/sanitizer-in-adoption-agency.html, sanitizer-custom-elements-constructor.html and
/// sethtml-with-declarative-shadow-root.tentative.html.
/// </summary>
internal sealed partial class HtmlTreeBuilder
{
    // The elements created by the parser that are removed or replaced by their children
    private Dictionary<Element, SanitizerElementAction>? _sanitizerActions;

    /// <summary>
    /// Gets or sets the sanitizer configuration (with the unsafe elements and attributes removed for a safe sanitization)
    /// applied while parsing, or null.
    /// </summary>
    public SanitizerConfiguration? Sanitizer
    {
        get;
        set
        {
            // (the actions of the elements created by a previous sanitizer are kept: a template with the sanitize attribute changes
            // the sanitizer of its content, see BeginPatchSanitization)
            field = value;
            if (value is not null)
            {
                _sanitizerActions ??= new(ReferenceEqualityComparer.Instance);
            }
        }
    }

    private SanitizerElementAction GetSanitizerAction(Element element) =>
        _sanitizerActions is not null && _sanitizerActions.TryGetValue(element, out var action) ? action : SanitizerElementAction.Keep;

    /// <summary>
    /// Returns the parent where the children of <paramref name="parent"/> are inserted: the nearest element of the stack of
    /// open elements, from <paramref name="parent"/>, that is not replaced by its children. The parsed children of a template
    /// element are its template contents, which are not kept when the template is replaced by its children.
    /// </summary>
    private Node SkipElementsReplacedWithChildren(Node parent)
    {
        if (parent is not Element element || !IsReplacedWithChildren(element))
            return parent;

        var index = _openElements.LastIndexOf(element);
        if (index < 0)
            return parent;

        while (index > 0 && IsReplacedWithChildren(_openElements[index]))
        {
            index--;
        }

        return _openElements[index];

        bool IsReplacedWithChildren(Element element) => GetSanitizerAction(element) == SanitizerElementAction.ReplaceWithChildren && !element.IsHtml(TagId.Template);
    }

    /// <summary>Returns a copy of the token of a kept element without the attributes removed by the sanitizer.</summary>
    private HtmlTagToken GetSanitizedToken(HtmlTagToken token, string @namespace)
    {
        if (Sanitizer is not { } sanitizer || token.Attributes.Length == 0)
            return token;

        var elementName = new SanitizerName(token.Name, @namespace);
        var local = sanitizer.FindElement(elementName);
        var attributes = new List<HtmlTokenAttribute>(token.Attributes.Length);
        foreach (ref var attribute in token.Attributes)
        {
            if (!sanitizer.RemovesAttribute(elementName, local, attribute.LocalName, attribute.Namespace, attribute.Value))
            {
                attributes.Add(attribute);
            }
        }

        if (attributes.Count == token.Attributes.Length)
            return token;

        return new HtmlTagToken(token.Name, token.TagId, [.. attributes], token.SelfClosing);
    }
}
