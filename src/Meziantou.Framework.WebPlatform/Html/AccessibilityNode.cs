using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A node of a snapshot of the accessibility tree, created by <see cref="Element.CaptureAccessibilityTree"/>: an element
/// with a role, or a run of text. The snapshot does not follow the later changes of the document.
/// </summary>
public sealed class AccessibilityNode
{
    private static readonly AccessibilityNode[] NoChildren = [];

    internal AccessibilityNode(Node node, AccessibilityProperties properties)
    {
        Node = node;
        Properties = properties;
    }

    /// <summary>
    /// The DOM node of the accessibility node: the <see cref="Element"/>, or, for a run of text, its first <see cref="Text"/>
    /// node (the element whose <c>::before</c> or <c>::after</c> content is the text when there is no text node).
    /// </summary>
    public Node Node { get; }

    /// <summary>The role, the name, the description and the states of the node.</summary>
    public AccessibilityProperties Properties { get; }

    /// <summary>The parent node, or <see langword="null"/> for the root of the snapshot.</summary>
    public AccessibilityNode? Parent { get; private set; }

    /// <summary>
    /// The children of the node, in the order of the accessibility tree: the flat tree (the shadow trees and their slotted
    /// nodes), with the relocations of <c>aria-owns</c>. The elements without a role, the presentational elements and the
    /// generic elements (e.g. <c>div</c>, <c>span</c>) are not nodes: their children are children of their parent.
    /// </summary>
    public IReadOnlyList<AccessibilityNode> Children { get; private set; } = NoChildren;

    internal void SetChildren(List<AccessibilityNode> children)
    {
        if (children.Count == 0)
            return;

        foreach (var child in children)
        {
            child.Parent = this;
        }

        Children = children.ToArray();
    }

    /// <summary>
    /// Returns the subtree in the YAML format of the aria snapshots of Playwright (https://playwright.dev/docs/aria-snapshots),
    /// e.g. <c>- heading "Title" [level=1]</c>. The children of a root without a role, presentational or generic, are written
    /// at the first level.
    /// </summary>
    public string ToAriaSnapshot() => AriaSnapshotWriter.Write(this);

    /// <summary>Returns the role, the name and the states of the node, as the key of a line of <see cref="ToAriaSnapshot"/>.</summary>
    public override string ToString() => AriaSnapshotWriter.GetKey(this);
}
