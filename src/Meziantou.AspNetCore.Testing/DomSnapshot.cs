using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Snapshots of DOM subtrees: their HTML formatted for comparisons, and their accessibility tree.</summary>
public static class DomSnapshot
{
    /// <summary>Gets the HTML of nodes, formatted for snapshots (see <see cref="MarkupOptions"/>).</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The HTML.</returns>
    public static string GetMarkup(IReadOnlyList<Node> nodes, MarkupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return MarkupFormatter.Format(nodes, options);
    }

    /// <summary>Gets the accessibility tree of elements, as a YAML snapshot (the format of the ARIA snapshots of Playwright).</summary>
    /// <param name="nodes">The nodes; the nodes that are not elements are ignored.</param>
    /// <returns>The snapshot.</returns>
    public static string GetAccessibilitySnapshot(IEnumerable<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return string.Join('\n', nodes.OfType<Element>().Select(element => element.CaptureAccessibilityTree()?.ToAriaSnapshot()).Where(snapshot => !string.IsNullOrEmpty(snapshot)));
    }
}
