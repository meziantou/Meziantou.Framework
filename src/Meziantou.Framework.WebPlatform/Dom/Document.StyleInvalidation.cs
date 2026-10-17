using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of a change recorded by <see cref="StyleInvalidationLog"/>.</summary>
internal enum StyleInvalidationKind : byte
{
    /// <summary>An attribute of an element changed.</summary>
    Attribute,

    /// <summary>A node was inserted in an element.</summary>
    ChildInserted,

    /// <summary>A node was removed from an element.</summary>
    ChildRemoved,

    /// <summary>The data of a text node child of an element changed.</summary>
    Text,

    /// <summary>The state of an element changed (see <see cref="StyleInvalidationStates"/>).</summary>
    State,
}

/// <summary>The states of an element that the user action pseudo-classes and <c>:state()</c> match.</summary>
[Flags]
internal enum StyleInvalidationStates : byte
{
    None = 0,

    /// <summary><c>:hover</c>.</summary>
    Hover = 1,

    /// <summary><c>:active</c>.</summary>
    Active = 2,

    /// <summary><c>:focus</c>, <c>:focus-visible</c> and <c>:focus-within</c>.</summary>
    Focus = 4,

    /// <summary><c>:state()</c>.</summary>
    CustomState = 8,
}

/// <summary>A change of a document recorded by <see cref="StyleInvalidationLog"/>.</summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Target">The element whose attribute or state changed, or the parent of the inserted, removed or changed node.</param>
/// <param name="Child">The inserted or removed node.</param>
/// <param name="Name">The local name of the changed attribute.</param>
/// <param name="Namespace">The namespace of the changed attribute.</param>
/// <param name="OldValue">The previous value of the attribute.</param>
/// <param name="NewValue">The new value of the attribute.</param>
/// <param name="States">The changed states.</param>
internal readonly record struct StyleInvalidation(StyleInvalidationKind Kind, Node Target, Node? Child = null, string? Name = null, string? Namespace = null, string? OldValue = null, string? NewValue = null, StyleInvalidationStates States = StyleInvalidationStates.None);

/// <summary>
/// The changes of the connected nodes of a document since the computed styles were last updated, which the style engine of the
/// document uses to invalidate the styles of the affected elements only (see <c>StyleEngine.Invalidation.cs</c>). A change
/// that is not described (<see cref="Document.IncrementDomVersion()"/>, <see cref="Document.IncrementElementStateVersion()"/>)
/// or too many changes make the log full: all the styles are then invalidated.
/// </summary>
internal sealed class StyleInvalidationLog
{
    /// <summary>The number of changes from which all the styles are invalidated (e.g. while the parser builds the document).</summary>
    private const int MaxCount = 1000;

    private readonly List<StyleInvalidation> _entries = [];

    /// <summary>Whether a change is not described by the log: all the styles must be invalidated.</summary>
    public bool IsFull { get; private set; }

    public List<StyleInvalidation> Entries => _entries;

    public void Add(StyleInvalidation entry)
    {
        if (IsFull)
            return;

        if (_entries.Count >= MaxCount)
        {
            SetFull();
            return;
        }

        _entries.Add(entry);
    }

    public void SetFull()
    {
        IsFull = true;
        _entries.Clear();
    }

    public void Clear()
    {
        IsFull = false;
        _entries.Clear();
    }
}

public partial class Document
{
    /// <summary>The changes of the document since the computed styles were last updated (see <see cref="StyleInvalidationLog"/>).</summary>
    internal StyleInvalidationLog StyleInvalidations { get; } = new();

    /// <summary>Records the change of an attribute of an element and increments <see cref="DomVersion"/>.</summary>
    internal void OnAttributeChangedForStyles(Element element, string localName, string? @namespace, string? oldValue, string? newValue)
    {
        if (element.IsConnected)
        {
            StyleInvalidations.Add(new StyleInvalidation(StyleInvalidationKind.Attribute, element, Name: localName, Namespace: @namespace, OldValue: oldValue, NewValue: newValue));
        }

        IncrementDomVersionCore();
    }

    /// <summary>Records the change of the data of a character data node and increments <see cref="DomVersion"/>.</summary>
    internal void OnCharacterDataChangedForStyles(CharacterData node)
    {
        if (node.ParentNode is { IsConnected: true } parent)
        {
            // (the comments and the processing instructions do not change the styles, except the xml-stylesheet ones: their
            // style sheets are compared)
            if (node is Text)
            {
                StyleInvalidations.Add(new StyleInvalidation(StyleInvalidationKind.Text, parent, node));
            }
            else if (node is ProcessingInstruction)
            {
                StyleInvalidations.SetFull();
            }
        }

        IncrementDomVersionCore();
    }

    /// <summary>
    /// Records the change of a state of elements (see <see cref="StyleInvalidationStates"/>) and increments
    /// <see cref="ElementStateVersion"/>: the elements are <paramref name="previous"/> and <paramref name="current"/>, and, for the
    /// user action pseudo-classes, their flat tree ancestors and the labeled controls of the label elements among them.
    /// </summary>
    internal void OnElementStateChangedForStyles(Element? previous, Element? current, StyleInvalidationStates states)
    {
        AddStateChanges(previous, states);
        AddStateChanges(current, states);
        IncrementElementStateVersionCore();
    }

    /// <summary>
    /// Records the changes of the user action pseudo-classes of the ancestors of a removed node: they stop matching
    /// <c>:hover</c>, <c>:active</c> or <c>:focus-within</c> when it contains the designated, the actively pointed or the focused
    /// element (the state of the document is updated after the removal).
    /// </summary>
    private void OnElementRemovedForStyles(Node parent, Node removed)
    {
        var states = StyleInvalidationStates.None;
        if (FocusedArea is { } focused && removed.IsShadowIncludingInclusiveAncestorOf(focused))
        {
            states |= StyleInvalidationStates.Focus;
        }

        if (DesignatedElement is { } designated && removed.IsShadowIncludingInclusiveAncestorOf(designated))
        {
            states |= StyleInvalidationStates.Hover;
        }

        if (ActivelyPointedElement is { } pointed && removed.IsShadowIncludingInclusiveAncestorOf(pointed))
        {
            states |= StyleInvalidationStates.Active;
        }

        if (states == StyleInvalidationStates.None)
            return;

        // The labeled controls of the removed labels stop matching too
        for (Node? node = removed; node is not null; node = node.GetNextInTreeOrder(removed))
        {
            if (node is HtmlLabelElement)
            {
                StyleInvalidations.SetFull();
                return;
            }
        }

        AddStateChanges(parent as Element, states);
    }

    private void AddStateChanges(Element? element, StyleInvalidationStates states)
    {
        if (element is null || !element.IsConnected || !ReferenceEquals(element.NodeDocument, this))
            return;

        if (states == StyleInvalidationStates.CustomState)
        {
            StyleInvalidations.Add(new StyleInvalidation(StyleInvalidationKind.State, element, States: states));
            return;
        }

        for (Node? node = element; node is not null; node = Dom.HtmlElement.GetFlatTreeParent(node))
        {
            if (node is not Element ancestor)
                continue;

            StyleInvalidations.Add(new StyleInvalidation(StyleInvalidationKind.State, ancestor, States: states));
            if (ancestor is HtmlLabelElement { Control: { } control })
            {
                StyleInvalidations.Add(new StyleInvalidation(StyleInvalidationKind.State, control, States: states));
            }
        }
    }
}
