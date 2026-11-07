using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The changes recorded for the box tree (see BoxTreeChanges): the layout engine patches its box tree with the boxes of the
// elements whose styles were discarded and of the elements whose attributes or children changed, instead of building it again.
internal sealed partial class StyleEngine
{
    /// <summary>The number of elements from which a set of changes is not recorded: the box tree is built again.</summary>
    private const int MaxBoxTreeChanges = 1000;

    /// <summary>The changes since the box tree of the document was built (or patched), or null when they are not recorded (see <see cref="TrackBoxTreeChanges"/>).</summary>
    private BoxTreeChanges? _boxTreeChanges;

    /// <summary>Starts recording the changes for the box tree of the document, discarding the changes recorded so far.</summary>
    internal void TrackBoxTreeChanges() => _boxTreeChanges = new BoxTreeChanges();

    /// <summary>Returns the changes recorded since <see cref="TrackBoxTreeChanges"/> (null when they were not recorded), and stops recording them.</summary>
    internal BoxTreeChanges? TakeBoxTreeChanges()
    {
        var changes = _boxTreeChanges;
        _boxTreeChanges = null;
        return changes;
    }

    /// <summary>Records that the boxes of an element (and of its descendants) may have changed: its styles or the styles of its pseudo-elements were discarded, or its attributes or its children changed.</summary>
    private void AddBoxTreeChange(Element element)
    {
        if (_boxTreeChanges is not { IsFull: false } changes)
            return;

        // The boxes of the summary of the user agent shadow tree of a details element are built with the boxes of the details element
        if (element.ParentNode is ShadowRoot { Host: HtmlDetailsElement details } && ReferenceEquals(details.CreatedImplicitSummary, element))
        {
            element = details;
        }

        changes.Elements.Add(element);
        if (changes.Elements.Count > MaxBoxTreeChanges)
        {
            changes.SetFull();
        }
    }

    /// <summary>Records that the boxes of the whole document may have changed (all the styles were discarded, or a change is not described).</summary>
    private void AddFullBoxTreeChange() => _boxTreeChanges?.SetFull();

    /// <summary>Records the changes of the document described by the log of the document (see <see cref="StyleInvalidationLog"/>).</summary>
    private void AddBoxTreeChanges(List<StyleInvalidation> changes)
    {
        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                // (the states only change the styles: they are recorded when the styles are discarded)
                case StyleInvalidationKind.State:
                    break;

                case StyleInvalidationKind.Attribute or StyleInvalidationKind.Text or StyleInvalidationKind.ChildInserted or StyleInvalidationKind.ChildRemoved when change.Target is Element element:
                    AddBoxTreeChange(element);
                    break;

                // The children of the document (a comment or a doctype does not generate boxes)
                default:
                    if (change.Child is not (Comment or DocumentType))
                    {
                        AddFullBoxTreeChange();
                    }

                    break;
            }
        }
    }
}
