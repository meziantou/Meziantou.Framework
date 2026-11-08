using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Element
{
    /// <summary>
    /// The computed role of the element (https://w3c.github.io/core-aam/#roleMappingComputedRole), as reported by the
    /// WebDriver "Get Computed Role" command: the first valid token of the role attribute, or the implicit role of HTML-AAM and
    /// SVG-AAM (e.g. "button", "heading", "generic", "none"). The empty string means that the element has no role. An element
    /// that is not rendered because of <c>display: none</c> (on it or on an ancestor) is not in the accessibility tree: its
    /// computed role is "none", as in the browsers.
    /// </summary>
    [NotInIdl("Computed role of the accessibility tree (WebDriver Get Computed Role), not exposed to scripts")]
    public string ComputedRole => Accessibility.GetComputedRole(this);

    /// <summary>
    /// The accessible name of the element (https://w3c.github.io/accname/), as reported by the WebDriver "Get Computed Label"
    /// command: aria-labelledby, aria-label, the native text alternatives (label, alt, caption, legend...), the name from
    /// content, then the title. The white space is collapsed and trimmed.
    /// </summary>
    [NotInIdl("Accessible name of the accessibility tree (WebDriver Get Computed Label), not exposed to scripts")]
    public string ComputedName => Accessibility.GetName(this);

    /// <summary>
    /// Returns the properties that the accessibility tree exposes for the element: its role, its accessible name and
    /// description, and its states (checked, pressed, expanded, selected, disabled, level). The values are computed when the
    /// method is called; use <see cref="CaptureAccessibilityTree"/> for the whole subtree.
    /// </summary>
    [NotInIdl("Properties of the accessibility tree (WebDriver accessibility properties), not exposed to scripts")]
    public AccessibilityProperties GetAccessibilityProperties() => Accessibility.GetProperties(this);

    /// <summary>
    /// Captures the accessibility tree rooted at the element: a snapshot that does not follow the later changes of the
    /// document, in the format of the aria snapshots of Playwright (<see cref="AccessibilityNode.ToAriaSnapshot"/>). The hidden
    /// subtrees are excluded; the elements without a role, the presentational elements and the generic elements are replaced by
    /// their children. Returns <see langword="null"/> when the element is hidden.
    /// </summary>
    [NotInIdl("Snapshot of the accessibility tree (as Playwright aria snapshots), not exposed to scripts")]
    public AccessibilityNode? CaptureAccessibilityTree() => Accessibility.CaptureTree(this);
}
