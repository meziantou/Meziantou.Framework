using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The changes of the computed styles and of the document since the box tree of a document was built, recorded by the style
/// engine (see <c>StyleEngine.BoxTreeChanges.cs</c>): the elements whose styles (or the styles of their pseudo-elements) were
/// discarded, and the elements whose attributes, children or texts changed. The boxes of these elements and of their
/// descendants are built again and replace their previous boxes (see <see cref="BoxTreeBuilder.TryPatch"/>); the other boxes
/// are kept. When the changes are not known element by element (<see cref="IsFull"/>: all the styles were discarded, or a change
/// is not described), the box tree is built again.
/// </summary>
internal sealed class BoxTreeChanges
{
    /// <summary>The elements whose boxes may have changed, with the boxes of their descendants.</summary>
    public HashSet<Element> Elements { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether the changes are not known element by element: the box tree must be built again.</summary>
    public bool IsFull { get; private set; }

    public void SetFull()
    {
        IsFull = true;
        Elements.Clear();
    }
}
