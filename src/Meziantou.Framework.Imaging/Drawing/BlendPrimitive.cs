using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that draws its first input over its second one with a blend mode: <c>feBlend</c> of SVG.</summary>
/// <remarks>
/// The first input is the source (<c>in</c>) and the second one the backdrop (<c>in2</c>), as in "Compositing and
/// Blending Level 1": with colors <c>cs</c> and <c>cb</c> multiplied by their alpha <c>as</c> and <c>ab</c>, and the
/// blend <c>B</c> of the straight colors (<see cref="BlendMode"/>), the result is <c>cs (1 - ab) + cb (1 - as) + as ab B</c>,
/// of alpha <c>as + ab - as ab</c>.
/// </remarks>
public sealed class BlendPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="BlendPrimitive"/> class.</summary>
    /// <param name="mode">The blend mode.</param>
    /// <param name="input2">The backdrop: <c>in2</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="BlendMode"/>.</exception>
    public BlendPrimitive(BlendMode mode, FilterInput input2)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The blend mode is not valid.");

        Mode = mode;
        Input2 = input2;
    }

    /// <summary>Gets the blend mode.</summary>
    public BlendMode Mode { get; }

    /// <summary>Gets the source: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    /// <summary>Gets the backdrop: <c>in2</c>.</summary>
    public FilterInput Input2 { get; }

    internal override int InputCount => 2;

    internal override FilterInput GetInput(int index) => index switch
    {
        0 => Input,
        1 => Input2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var source = context.GetInput(0).Values;
        var backdrop = context.GetInput(1).Values;
        var values = result.Values;
        Span<double> sourceColor = stackalloc double[3];
        Span<double> backdropColor = stackalloc double[3];
        Span<double> blended = stackalloc double[3];
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            var offset = result.IndexOf(subregion.Left, y);
            for (var i = offset; i < offset + (4 * subregion.Width); i += 4)
            {
                double sa = source[i + 3];
                double ba = backdrop[i + 3];
                for (var channel = 0; channel < 3; channel++)
                {
                    sourceColor[channel] = sa > 0 ? Math.Clamp(source[i + channel] / sa, 0, 1) : 0;
                    backdropColor[channel] = ba > 0 ? Math.Clamp(backdrop[i + channel] / ba, 0, 1) : 0;
                }

                BlendFunctions.Blend(Mode, sourceColor, backdropColor, blended);
                for (var channel = 0; channel < 3; channel++)
                {
                    values[i + channel] = (float)((source[i + channel] * (1 - ba)) + (backdrop[i + channel] * (1 - sa)) + (sa * ba * blended[channel]));
                }

                values[i + 3] = (float)(sa + ba - (sa * ba));
            }
        }
    }
}
