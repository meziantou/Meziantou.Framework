namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The items of a multi-line flex container or of a grid container that start a new flex line or a new grid row in the strip
/// of a page of another inline size (see <see cref="LayoutContext.RowResumes"/>): the first of the items of each set, in the
/// order of the items, starts a line or a row.
/// </summary>
/// <param name="Following">The items after the break.</param>
/// <param name="Crossed">The items of the flex line or of the grid row that the break crosses, or null (a break between two lines or rows).</param>
internal sealed record RowResume(HashSet<ContainerBox> Following, HashSet<ContainerBox>? Crossed = null)
{
    /// <summary>Returns whether an item starts a line or a row: it is the first item of one of the sets, after an item that is not in it.</summary>
    public bool StartsRow(ContainerBox item, ContainerBox? previous)
    {
        return (Following.Contains(item) && (previous is null || !Following.Contains(previous)))
            || (Crossed is not null && Crossed.Contains(item) && (previous is null || !Crossed.Contains(previous)));
    }
}
