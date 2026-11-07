using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that repeats the subregion of its input in every direction: <c>feTile</c> of SVG.</summary>
/// <remarks>
/// The tile is the subregion of the input, in pixels of the canvas; it is in place where it is, and its copies are side
/// by side around it. By default, the subregion of the primitive is the one of its input: give it a
/// <see cref="FilterPrimitive.Subregion"/> to fill more.
/// </remarks>
public sealed class TilePrimitive : FilterPrimitive
{
    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    // The copies of the tile can be anywhere
    internal override (double X, double Y) GetReach() => (DrawingGuard.MaxCoordinate, DrawingGuard.MaxCoordinate);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var input = context.GetInput(0);
        var tile = context.GetInputSubregion(0);
        if (tile.IsEmpty)
            return;

        var bounds = input.Bounds;

        var source = input.Values;
        var values = result.Values;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            // What the tile has beyond the surface is transparent
            var sourceY = tile.Top + Modulo(y - tile.Top, tile.Height);
            if (sourceY < bounds.Top || sourceY >= bounds.Bottom)
                continue;

            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                var sourceX = tile.Left + Modulo(x - tile.Left, tile.Width);
                if (sourceX >= bounds.Left && sourceX < bounds.Right)
                {
                    source.Slice(input.IndexOf(sourceX, sourceY), 4).CopyTo(values.Slice(result.IndexOf(x, y), 4));
                }
            }
        }

        static int Modulo(int value, int size)
        {
            var remainder = value % size;
            return remainder < 0 ? remainder + size : remainder;
        }
    }
}
