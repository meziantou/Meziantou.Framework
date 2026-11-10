using System.Collections.ObjectModel;
using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A Venn diagram: sets drawn as overlapping circles, whose areas and overlaps follow their sizes, with labels and texts
/// inside them (Mermaid <c>venn-beta</c>). The circles are laid out as Mermaid does, with the algorithm of venn.js.
/// </summary>
public sealed class VennDiagram : Diagram
{
    private double _width = 800;
    private double _height = 450;

    /// <summary>Gets or sets the width of the drawing, in pixels. The default is 800, as in Mermaid.</summary>
    /// <remarks>The texts, the outlines and the padding of the circles grow with the width: they are those of Mermaid at a width of 1600 pixels, scaled.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Width
    {
        get => _width;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The width must be a positive finite number.");

            _width = value;
        }
    }

    /// <summary>Gets or sets the height of the drawing, in pixels. The default is 450, as in Mermaid.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Height
    {
        get => _height;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The height must be a positive finite number.");

            _height = value;
        }
    }

    /// <summary>Gets the sets and their overlaps, in the order they are drawn: the circles take the colors of the sets in this order.</summary>
    public IList<VennArea> Areas { get; } = new List<VennArea>();

    /// <summary>Gets the texts placed inside the areas.</summary>
    public IList<VennTextNode> TextNodes { get; } = new List<VennTextNode>();

    /// <summary>Gets the styles of the areas and of the texts, applied in order.</summary>
    public IList<VennStyleRule> Styles { get; } = new List<VennStyleRule>();

    /// <summary>
    /// Gets the colors of the sets that replace the ones of the theme, by index from 0 to 7 (Mermaid <c>venn1</c> to
    /// <c>venn8</c>): the n-th set takes the color n modulo 8.
    /// </summary>
    public IDictionary<int, SceneColor> SetColors { get; } = new Dictionary<int, SceneColor>();

    /// <summary>Gets or sets the color of the labels of the overlaps and of the texts, or <see langword="null"/> for the text color of the theme (Mermaid <c>vennSetTextColor</c>).</summary>
    public SceneColor? TextColor { get; set; }

    /// <summary>Adds a set.</summary>
    /// <param name="id">The identifier of the set.</param>
    /// <param name="label">The label, or <see langword="null"/> to show the identifier.</param>
    /// <param name="size">The size, or <see langword="null"/> for 10.</param>
    /// <returns>The set.</returns>
    public VennArea AddSet(string id, string? label = null, double? size = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        var area = new VennArea([id], label, size);
        Areas.Add(area);
        return area;
    }

    /// <summary>Adds the overlap of sets (Mermaid <c>union</c>).</summary>
    /// <param name="sets">The identifiers of the sets, at least two.</param>
    /// <param name="label">The label, or <see langword="null"/> for none.</param>
    /// <param name="size">The size, or <see langword="null"/> for 10 divided by the square of the number of sets.</param>
    /// <returns>The overlap.</returns>
    /// <exception cref="ArgumentException"><paramref name="sets"/> has fewer than two identifiers.</exception>
    public VennArea AddOverlap(IEnumerable<string> sets, string? label = null, double? size = null)
    {
        var area = new VennArea(sets, label, size);
        if (area.Sets.Count < 2)
            throw new ArgumentException("An overlap needs at least two sets.", nameof(sets));

        Areas.Add(area);
        return area;
    }

    /// <summary>Adds a text inside an area.</summary>
    /// <param name="sets">The identifiers of the sets of the area.</param>
    /// <param name="id">The identifier of the text.</param>
    /// <param name="label">The text shown, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The text.</returns>
    public VennTextNode AddText(IEnumerable<string> sets, string id, string? label = null)
    {
        var node = new VennTextNode(sets, id, label);
        TextNodes.Add(node);
        return node;
    }

    /// <summary>Adds a style to an area, or to a text.</summary>
    /// <param name="targets">The identifiers of the sets of the area, or the identifier of the text.</param>
    /// <param name="style">The style.</param>
    /// <returns>The rule.</returns>
    public VennStyleRule AddStyle(IEnumerable<string> targets, VennStyle style)
    {
        var rule = new VennStyleRule(targets, style);
        Styles.Add(rule);
        return rule;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => VennDiagramRenderer.Render(this, context, group);

    internal static ReadOnlyCollection<string> SortIdentifiers(IEnumerable<string> identifiers, string paramName)
    {
        ArgumentNullException.ThrowIfNull(identifiers, paramName);
        var list = identifiers.ToList();
        foreach (var identifier in list)
        {
            if (identifier is null)
                throw new ArgumentException("An identifier cannot be null.", paramName);
        }

        list.Sort(StringComparer.Ordinal);
        return list.AsReadOnly();
    }
}
