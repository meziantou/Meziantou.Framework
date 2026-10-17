using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The named elements of a view transition (https://drafts.csswg.org/css-view-transitions-1/#viewtransition-named-elements): an
/// ordered map of the captured elements by view transition name, in the order of the captures (the paint order of the old
/// state, then the names that only exist in the new state).
/// </summary>
internal sealed class ViewTransitionNamedElements
{
    private readonly List<ViewTransitionCapturedElement> _values = [];
    private readonly Dictionary<string, ViewTransitionCapturedElement> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<ViewTransitionCapturedElement> Values => _values;

    public int Count => _values.Count;

    public ViewTransitionCapturedElement? Get(string name) => _byName.GetValueOrDefault(name);

    public void Add(ViewTransitionCapturedElement captured)
    {
        _byName.Add(captured.Name, captured);
        _values.Add(captured);
    }

    public void Remove(ViewTransitionCapturedElement captured)
    {
        _byName.Remove(captured.Name);
        _values.Remove(captured);
    }
}

/// <summary>https://drafts.csswg.org/css-view-transitions-1/#captured-element: the old and the new state of a named element of a view transition.</summary>
internal sealed class ViewTransitionCapturedElement
{
    /// <summary>The view transition name: the serialization of the identifier (as in the argument of the pseudo-elements), or a generated name.</summary>
    public required string Name { get; init; }

    /// <summary>The image of the old state, or null when the name only exists in the new state.</summary>
    public ViewTransitionSnapshot? OldImage { get; set; }

    /// <summary>Whether the old state was captured (the image is released when the transition ends).</summary>
    public bool HasOldImage { get; set; }

    public double OldWidth { get; set; }

    public double OldHeight { get; set; }

    /// <summary>The transform that maps the border box of the old element from the origin of the snapshot containing block to its visual position (a 4x4 matrix in column-major order).</summary>
    public double[] OldTransform { get; set; } = ViewTransitionCapture.Identity();

    public string? OldWritingMode { get; set; }

    public string? OldDirection { get; set; }

    public string? OldTextOrientation { get; set; }

    public string? OldMixBlendMode { get; set; }

    public string? OldBackdropFilter { get; set; }

    public string? OldColorScheme { get; set; }

    /// <summary>The widths of the borders of the old element (the origin of its nested groups is its padding box).</summary>
    public Sides<double> OldBorderWidth { get; set; }

    /// <summary>The declarations of the shape of the border of the old element (its radii and its corner shapes), for the <c>::view-transition-group-children()</c> of its group.</summary>
    public string? OldBorderShape { get; set; }

    /// <summary>The declarations of the shape of the border of the new element.</summary>
    public string? NewBorderShape { get; set; }

    /// <summary>The element of the new state, or null when the name only exists in the old state.</summary>
    public Element? NewElement { get; set; }

    /// <summary>The pseudo-element of <see cref="NewElement"/> that is captured (e.g. "backdrop"), or null for the element itself.</summary>
    public string? NewPseudoElement { get; set; }

    /// <summary>The natural size of the image of the new element: the size of its border box (or of the snapshot containing block for the document element).</summary>
    public PhysicalSize NewSize { get; set; }

    /// <summary>The origin of the image of the new element in the coordinates of its painting (see <see cref="ViewTransitionSnapshot.Origin"/>).</summary>
    public PhysicalOffset NewOrigin { get; set; }

    /// <summary>The widths of the borders of the new element.</summary>
    public Sides<double> NewBorderWidth { get; set; }

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#captured-element-class-list: the computed <c>view-transition-class</c> (of the new element when there is one).</summary>
    public IReadOnlyList<string> ClassList { get; set; } = [];

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#captured-element-containing-group-name: the name of the group that contains the group of the element, or null.</summary>
    public string? ContainingGroupName { get; set; }

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#captured-element-transform-from-snapshot-containing-block</summary>
    public double[]? TransformFromSnapshotContainingBlock { get; set; }
}

/// <summary>
/// The image of an element captured by a view transition (https://drafts.csswg.org/css-view-transitions-1/#capture-the-image):
/// its natural size is the size of the border box of the element (the snapshot containing block for the document element),
/// and it also contains the ink overflow of the element, outside of its natural bounds.
/// </summary>
internal sealed class ViewTransitionSnapshot : IDisposable
{
    /// <summary>The natural size of the image, in CSS px.</summary>
    public required PhysicalSize NaturalSize { get; init; }

    /// <summary>The captured area (see <see cref="Origin"/>): the natural bounds and the ink overflow.</summary>
    public required PhysicalRect Bounds { get; init; }

    /// <summary>
    /// The position of the origin of the image (the origin of the border box of the element) in the coordinates of
    /// <see cref="Content"/> and of <see cref="Bounds"/>: the fractional part of the position of the element in the viewport, so
    /// that the painting is snapped to the same device pixels as in the document.
    /// </summary>
    public PhysicalOffset Origin { get; init; }

    /// <summary>The painting of the element (see <see cref="Origin"/>).</summary>
    public required DisplayList Content { get; init; }

    /// <summary>The pixels of <see cref="Bounds"/> rasterized by the rendering backend, or null without backend (the painting is drawn instead).</summary>
    public PaintImage? Image { get; init; }

    /// <summary>The bitmap of <see cref="Image"/>, released with the snapshot.</summary>
    public RenderingImage? Bitmap { get; init; }

    public void Dispose() => Bitmap?.Dispose();
}

/// <summary>
/// The frame of the root element of a scoped view transition displayed while its rendering is suppressed
/// (https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions: "rendering is paused within the view transition
/// root element"): the painting of its stacking context when its old state was captured.
/// </summary>
/// <param name="Content">The painting of the element, with its effects and without its transform.</param>
/// <param name="Origin">The position of the origin of the border box of the element in <paramref name="Content"/>.</param>
/// <param name="ContentSize">The size of the content box of the element: its size containment keeps it while the rendering is suppressed.</param>
internal sealed record ViewTransitionFrozenFrame(DisplayList Content, PhysicalOffset Origin, PhysicalSize ContentSize);

/// <summary>A <c>::view-transition-group()</c> of the pseudo-element tree of a view transition, with its image pair.</summary>
internal sealed class ViewTransitionGroup
{
    public required string Name { get; init; }

    public required ViewTransitionCapturedElement Captured { get; init; }

    /// <summary>Whether the image pair has a <c>::view-transition-old()</c>.</summary>
    public bool HasOld => Captured.HasOldImage;

    /// <summary>Whether the image pair has a <c>::view-transition-new()</c>.</summary>
    public bool HasNew => Captured.NewElement is not null;

    /// <summary>The group whose <c>::view-transition-group-children()</c> contains this group, or null for a child of <c>::view-transition</c>.</summary>
    public ViewTransitionGroup? Parent { get; set; }

    /// <summary>The groups nested in the <c>::view-transition-group-children()</c> of this group, in order.</summary>
    public List<ViewTransitionGroup> Children { get; } = [];
}

/// <summary>
/// The view transition tree (https://drafts.csswg.org/css-view-transitions-1/#view-transition-tree): the pseudo-elements of an
/// active view transition, which originate from its root element. The pseudo-elements are named as the styles and the boxes
/// name them: <c>view-transition</c>, <c>view-transition-group(name)</c>, <c>view-transition-image-pair(name)</c>,
/// <c>view-transition-old(name)</c>, <c>view-transition-new(name)</c> and <c>view-transition-group-children(name)</c>.
/// </summary>
internal sealed class ViewTransitionPseudoTree
{
    public const string RootName = "view-transition";
    public const string GroupPrefix = "view-transition-group(";
    public const string GroupChildrenPrefix = "view-transition-group-children(";
    public const string ImagePairPrefix = "view-transition-image-pair(";
    public const string OldPrefix = "view-transition-old(";
    public const string NewPrefix = "view-transition-new(";

    private readonly Dictionary<string, ViewTransitionGroup> _byName = new(StringComparer.Ordinal);

    private ViewTransitionPseudoTree(Element root)
    {
        Root = root;
    }

    /// <summary>The originating element of the pseudo-elements: the root element of the transition.</summary>
    public Element Root { get; }

    /// <summary>The groups that are children of <c>::view-transition</c>, in order.</summary>
    public List<ViewTransitionGroup> Groups { get; } = [];

    public ViewTransitionGroup? FindGroup(string name) => _byName.GetValueOrDefault(name);

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#setup-transition-pseudo-elements</summary>
    public static ViewTransitionPseudoTree Create(ViewTransition transition)
    {
        var tree = new ViewTransitionPseudoTree(transition.RootElement!);
        foreach (var captured in transition.NamedElements.Values)
        {
            tree._byName[captured.Name] = new ViewTransitionGroup { Name = captured.Name, Captured = captured };
        }

        // https://drafts.csswg.org/css-view-transitions-2/#vt-group-reparent: a group is nested in the
        // ::view-transition-group-children() of its containing group
        foreach (var captured in transition.NamedElements.Values)
        {
            var group = tree._byName[captured.Name];
            if (captured.ContainingGroupName is { } containing && tree._byName.TryGetValue(containing, out var parent) && !ReferenceEquals(parent, group) && !IsDescendantOf(parent, group))
            {
                group.Parent = parent;
                parent.Children.Add(group);
            }
            else
            {
                tree.Groups.Add(group);
            }
        }

        return tree;

        static bool IsDescendantOf(ViewTransitionGroup group, ViewTransitionGroup ancestor)
        {
            for (var current = group.Parent; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Returns whether a name is the name of a view transition pseudo-element (with or without argument).</summary>
    public static bool IsViewTransitionPseudoElement(string? pseudoElement) => pseudoElement is not null && pseudoElement.StartsWith(RootName, StringComparison.Ordinal);

    /// <summary>Splits the name of a named view transition pseudo-element: "view-transition-old(name)" → ("view-transition-old", "name").</summary>
    public static bool TryParse(string pseudoElement, out string type, out string name)
    {
        var index = pseudoElement.IndexOf('(', StringComparison.Ordinal);
        if (index < 0 || !pseudoElement.EndsWith(')', StringComparison.Ordinal) || !IsViewTransitionPseudoElement(pseudoElement))
        {
            type = pseudoElement;
            name = "";
            return false;
        }

        type = pseudoElement[..index];
        name = pseudoElement[(index + 1)..^1];
        return true;
    }

    /// <summary>Returns whether the pseudo-element exists in the tree.</summary>
    public bool Contains(string pseudoElement)
    {
        if (pseudoElement == RootName)
            return true;

        if (!TryParse(pseudoElement, out var type, out var name) || FindGroup(name) is not { } group)
            return false;

        return type switch
        {
            "view-transition-group" or "view-transition-image-pair" => true,
            "view-transition-old" => group.HasOld,
            "view-transition-new" => group.HasNew,
            "view-transition-group-children" => group.Children.Count > 0,
            _ => false,
        };
    }

    /// <summary>
    /// Returns whether <c>:only-child</c> matches the pseudo-element (https://drafts.csswg.org/css-view-transitions-1/#pseudo-element-tree):
    /// it has no sibling in the tree.
    /// </summary>
    public bool IsOnlyChild(string pseudoElement)
    {
        if (!TryParse(pseudoElement, out var type, out var name) || FindGroup(name) is not { } group)
            return false;

        return type switch
        {
            "view-transition-group" => (group.Parent?.Children ?? Groups).Count == 1,
            "view-transition-image-pair" => group.Children.Count == 0,
            "view-transition-old" => group.HasOld && !group.HasNew,
            "view-transition-new" => group.HasNew && !group.HasOld,
            _ => false,
        };
    }

    /// <summary>
    /// Returns the name of the pseudo-element whose style a view transition pseudo-element inherits from (its parent in the
    /// tree), or null for <c>::view-transition</c>, which inherits from its originating element. A pseudo-element that is not
    /// in the tree inherits as in a tree without nested group.
    /// </summary>
    public static string? GetParent(ViewTransitionPseudoTree? tree, string pseudoElement)
    {
        if (!TryParse(pseudoElement, out var type, out var name))
            return null;

        return type switch
        {
            "view-transition-group" => tree?.FindGroup(name)?.Parent is { } parent ? GroupChildrenPrefix + parent.Name + ")" : RootName,
            "view-transition-image-pair" or "view-transition-group-children" => GroupPrefix + name + ")",
            "view-transition-old" or "view-transition-new" => ImagePairPrefix + name + ")",
            _ => null,
        };
    }

    private Dictionary<string, int>? _order;

    /// <summary>Returns the position of a pseudo-element in the tree order, or null when it is not in the tree.</summary>
    public int? GetOrder(string pseudoElement)
    {
        if (_order is null)
        {
            _order = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in EnumeratePseudoElements())
            {
                _order[name] = _order.Count;
            }
        }

        return _order.TryGetValue(pseudoElement, out var order) ? order : null;
    }

    /// <summary>Enumerates the names of the pseudo-elements of the tree, in tree order.</summary>
    public IEnumerable<string> EnumeratePseudoElements()
    {
        yield return RootName;
        foreach (var group in Groups)
        {
            foreach (var pseudoElement in EnumerateGroup(group))
            {
                yield return pseudoElement;
            }
        }
    }

    private static IEnumerable<string> EnumerateGroup(ViewTransitionGroup group)
    {
        yield return GroupPrefix + group.Name + ")";
        yield return ImagePairPrefix + group.Name + ")";
        if (group.HasOld)
        {
            yield return OldPrefix + group.Name + ")";
        }

        if (group.HasNew)
        {
            yield return NewPrefix + group.Name + ")";
        }

        if (group.Children.Count > 0)
        {
            yield return GroupChildrenPrefix + group.Name + ")";
            foreach (var child in group.Children)
            {
                foreach (var pseudoElement in EnumerateGroup(child))
                {
                    yield return pseudoElement;
                }
            }
        }
    }
}

/// <summary>The names generated for the elements whose <c>view-transition-name</c> is <c>auto</c> or <c>match-element</c> (https://drafts.csswg.org/css-view-transitions-2/#auto-vt-name).</summary>
internal static class ViewTransitionGeneratedNames
{
    private static readonly ConditionalWeakTable<Element, string> Names = [];
    private static long s_next;

    /// <summary>Returns the name of an element, unique and stable for the element.</summary>
    public static string Get(Element element) => Names.GetValue(element, static _ => "-ua-match-element-" + Interlocked.Increment(ref s_next).ToString(System.Globalization.CultureInfo.InvariantCulture));
}
