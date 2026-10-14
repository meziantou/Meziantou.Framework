using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that gives each component of each pixel of its input a new value by a function: <c>feComponentTransfer</c> of SVG.</summary>
/// <remarks>The functions are applied to the straight colors and to the alpha of each pixel (<see cref="TransferFunction"/>), and the colors are then multiplied by the new alpha.</remarks>
public sealed class ComponentTransferPrimitive : FilterPrimitive
{
    /// <summary>Gets the function of the red: <c>feFuncR</c>. Defaults to <see cref="TransferFunction.Identity"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TransferFunction Red { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = TransferFunction.Identity;

    /// <summary>Gets the function of the green: <c>feFuncG</c>. Defaults to <see cref="TransferFunction.Identity"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TransferFunction Green { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = TransferFunction.Identity;

    /// <summary>Gets the function of the blue: <c>feFuncB</c>. Defaults to <see cref="TransferFunction.Identity"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TransferFunction Blue { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = TransferFunction.Identity;

    /// <summary>Gets the function of the alpha: <c>feFuncA</c>. Defaults to <see cref="TransferFunction.Identity"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TransferFunction Alpha { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = TransferFunction.Identity;

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var input = context.GetInput(0).Values;
        var values = result.Values;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            var offset = result.IndexOf(subregion.Left, y);
            for (var i = offset; i < offset + (4 * subregion.Width); i += 4)
            {
                double alpha = input[i + 3];
                double red = 0, green = 0, blue = 0;
                if (alpha > 0)
                {
                    red = input[i] / alpha;
                    green = input[i + 1] / alpha;
                    blue = input[i + 2] / alpha;
                }

                var resultAlpha = Alpha.Evaluate(alpha);
                values[i] = (float)(Red.Evaluate(red) * resultAlpha);
                values[i + 1] = (float)(Green.Evaluate(green) * resultAlpha);
                values[i + 2] = (float)(Blue.Evaluate(blue) * resultAlpha);
                values[i + 3] = (float)resultAlpha;
            }
        }
    }
}
