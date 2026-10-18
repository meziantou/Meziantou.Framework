namespace Meziantou.Framework.WebPlatform.Css.Layout;

internal static partial class ColumnBreaker
{
    /// <summary>
    /// Returns by how much the items that start in a row of a grid move up when the fragmentainer starts in the gutter before the
    /// row, else 0. "The gap disappears when it coincides with a fragmentation break" (https://drafts.csswg.org/css-gaps-1/#gaps):
    /// the gutters that no item spans across are skipped by the flow of the container (<see cref="FragmentationFlow.SkipGap"/>,
    /// the next rows are shifted), but the content of the item that spans across the gutter cannot be removed. As in Blink, the
    /// row after the gutter then starts at the break (it grows by the part of the gutter that is after the break: its items move
    /// up, and the stretched ones grow), and the next rows keep their positions.
    /// </summary>
    /// <param name="group">The parallel flows of a box.</param>
    /// <param name="track">The index of the row.</param>
    /// <param name="position">The position of the strip where the content of the fragmentainer starts.</param>
    private static double GetSpannedGapSpillover(ParallelGroup group, int track, double position)
    {
        if (track == 0 || group.Container.Box.Display.Inside != DisplayInside.Grid)
            return 0;

        var gapStart = group.TrackEnds[track - 1];
        var gapEnd = group.TrackStarts[track];
        return position >= gapStart - Epsilon && position < gapEnd - Epsilon ? gapEnd - position : 0;
    }
}
