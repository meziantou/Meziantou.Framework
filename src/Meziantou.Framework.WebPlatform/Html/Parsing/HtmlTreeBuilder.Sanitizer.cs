using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// The sanitization of the nodes while they are parsed (setHTML(), setHTMLUnsafe(), parseHTML() and parseHTMLUnsafe()
/// with a sanitizer), as in Blink. The tree construction is unchanged, but the elements removed by the sanitizer are never
/// inserted (nor are their descendants), and the attributes removed by the sanitizer are never set. An element replaced by
/// its children is never inserted either: the parser records the location where it would be inserted (e.g. before a table
/// when it is foster-parented, or where the adoption agency algorithm moves it), and inserts its children there. So, the
/// sanitizer prevents the side effects of the parsing: the constructors of the removed custom elements do not run, the
/// declarative shadow roots of the removed or replaced elements are not attached, and the declarative shadow roots use the
/// sanitized attributes of their template element. The WPT tests are sanitizer-api/sanitizer-in-adoption-agency.html,
/// sethtml-tree-construction.html, sethtml-replace-with-children.html, sanitizer-custom-elements-constructor.html and
/// sethtml-with-declarative-shadow-root.html.
/// </summary>
internal sealed partial class HtmlTreeBuilder
{
    // The elements created by the parser that are removed or replaced by their children
    private Dictionary<Element, SanitizerElementAction>? _sanitizerActions;

    // The locations where the children of the elements replaced by their children are inserted
    private Dictionary<Element, (Node Parent, Node? Before)>? _replacedElementLocations;

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
    /// Whether the children of <paramref name="element"/> are inserted at its recorded location. The parsed children of a
    /// template element are its template contents, which are not kept when the template is replaced by its children.
    /// </summary>
    private bool IsReplacedWithChildren(Element element) => GetSanitizerAction(element) == SanitizerElementAction.ReplaceWithChildren && !element.IsHtml(TagId.Template);

    /// <summary>
    /// Returns the location where the nodes appended to <paramref name="parent"/> are inserted: the location of
    /// <paramref name="parent"/> when it is replaced by its children, or the end of <paramref name="parent"/>.
    /// </summary>
    private (Node Parent, Node? Before) GetChildInsertionLocation(Node parent)
    {
        if (parent is not Element element || !IsReplacedWithChildren(element))
            return (parent, null);

        if (_replacedElementLocations is not null && _replacedElementLocations.TryGetValue(element, out var location))
            return location;

        // An element that is not inserted yet: the nearest element of the stack of open elements that is not replaced
        var index = _openElements.LastIndexOf(element);
        if (index < 0)
            return (parent, null);

        while (index > 0 && IsReplacedWithChildren(_openElements[index]))
        {
            index--;
        }

        return GetChildInsertionLocation(_openElements[index]);
    }

    /// <summary>
    /// Inserts <paramref name="element"/> at <paramref name="location"/>, or moves it there: a kept element is inserted in
    /// the tree, an element replaced by its children records the location of its next children, and a removed element is
    /// never inserted.
    /// </summary>
    private void InsertElementAt(Element element, (Node Parent, Node? Before) location)
    {
        switch (GetSanitizerAction(element))
        {
            case SanitizerElementAction.Keep:
                var (parent, referenceChild) = location;
                element.RemoveFromParent();

                // A custom element reaction may have inserted the element in the tree (e.g. in one of its descendants): the
                // element is not inserted in one of its descendants, which would create a cycle
                if (element.ParentNode is null && !element.IsHostIncludingInclusiveAncestorOf(parent) &&
                    !(parent is Document document && document.DocumentElement is not null) && !IsDisplaced(parent, referenceChild))
                {
                    parent.InsertBeforeCore(element, referenceChild);
                }

                break;

            case SanitizerElementAction.ReplaceWithChildren when IsReplacedWithChildren(element):
                // The location is the appropriate place for inserting a node: the children of the root are created for the root
                // (see CreateElementForToken), and inserted in the root insertion target by GetAdjustedInsertionLocation
                if (RootInsertionTarget is not null && ReferenceEquals(location.Parent, RootInsertionTarget) && ReferenceEquals(location.Before, RootInsertionReferenceChild))
                {
                    location = (_openElements[0], null);
                }

                _replacedElementLocations ??= new(ReferenceEqualityComparer.Instance);
                _replacedElementLocations[element] = location;
                break;
        }
    }

    /// <summary>
    /// Moves the location of the elements replaced by their children located in <paramref name="oldParent"/> to
    /// <paramref name="newParent"/>, when the children of <paramref name="oldParent"/> are moved to <paramref name="newParent"/>
    /// (as their children would be). Only the elements of the stack of open elements have other children to insert.
    /// </summary>
    private void MoveReplacedElementLocations(Element oldParent, Element newParent)
    {
        if (_replacedElementLocations is null)
            return;

        foreach (var element in _openElements)
        {
            if (_replacedElementLocations.TryGetValue(element, out var location) && ReferenceEquals(location.Parent, oldParent))
            {
                _replacedElementLocations[element] = (newParent, location.Before is not null && ReferenceEquals(location.Before.ParentNode, newParent) ? location.Before : null);
            }
        }
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
