using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The used sizes of the tracks of a grid container in one axis and the names of its lines, recorded by the grid layout (see
/// <see cref="LayoutContext.OnGridLaidOut"/>) for the resolved value of <c>grid-template-columns</c> and <c>grid-template-rows</c>
/// (https://drafts.csswg.org/css-grid-2/#resolved-track-list).
/// </summary>
/// <param name="sizes">The used sizes of all the tracks, implicit and explicit, in the order of the lines (in px, zoomed: the coordinates of the fragment tree).</param>
/// <param name="lineNames">The names of the lines (one entry per line, so one more than the tracks; null or empty when a line has no names), or null when no line has a name.</param>
internal sealed class GridTrackSizes(double[] sizes, string[]?[]? lineNames, bool isSubgrid = false)
{
    /// <summary>
    /// Returns the resolved track list of a subgridded axis (https://drafts.csswg.org/css-grid-2/#resolved-track-list-subgrid): the
    /// <c>subgrid</c> keyword and the names explicitly defined on the subgrid for each of its lines.
    /// </summary>
    public static GridTrackSizes ForSubgrid(int trackCount, string[]?[]? lineNames) => new(new double[trackCount], lineNames, isSubgrid: true);

    /// <summary>The stacking axis of a grid lanes container (https://drafts.csswg.org/css-grid-3/): it has no tracks, its resolved value is the computed value.</summary>
    public static GridTrackSizes StackingAxis { get; } = new([], null);

    /// <summary>Whether the axis is a subgridded axis: the resolved value lists the line names of the subgrid, not the track sizes.</summary>
    public bool IsSubgrid { get; } = isSubgrid;

    /// <summary>The used sizes of the tracks, implicit and explicit, in the order of the lines (in px, zoomed).</summary>
    public double[] Sizes { get; } = sizes;

    /// <summary>The names of the lines (one more entry than <see cref="Sizes"/>), or null when no line has a name.</summary>
    public string[]?[]? LineNames { get; } = lineNames;

    /// <summary>
    /// Serializes the resolved track list (https://drafts.csswg.org/css-grid-2/#resolved-track-list-standalone): "every track
    /// listed individually, whether implicitly or explicitly created, without using the repeat() notation", the sizes in px in
    /// the coordinates of the element (divided by its effective zoom), the names of each line in brackets.
    /// </summary>
    public string Serialize(double zoom)
    {
        if (IsSubgrid)
        {
            // "serialized as the subgrid keyword followed by a list representing each of its lines as a line name set of all the
            // line's names explicitly defined on the subgrid [...] without using the repeat() notation"
            var subgrid = new StringBuilder("subgrid");
            for (var i = 0; i <= Sizes.Length; i++)
            {
                subgrid.Append(" [");
                if (LineNames is not null && i < LineNames.Length && LineNames[i] is { } lineNames)
                {
                    subgrid.AppendJoin(' ', lineNames);
                }

                subgrid.Append(']');
            }

            return subgrid.ToString();
        }

        if (Sizes.Length == 0)
            return "none";

        var builder = new StringBuilder();
        for (var i = 0; i <= Sizes.Length; i++)
        {
            if (LineNames is not null && i < LineNames.Length && LineNames[i] is { Length: > 0 } names)
            {
                (builder.Length > 0 ? builder.Append(' ') : builder).Append('[').AppendJoin(' ', names).Append(']');
            }

            if (i < Sizes.Length)
            {
                (builder.Length > 0 ? builder.Append(' ') : builder).Append(CssValueComputer.FormatPixels(Sizes[i] / zoom));
            }
        }

        return builder.ToString();
    }
}
