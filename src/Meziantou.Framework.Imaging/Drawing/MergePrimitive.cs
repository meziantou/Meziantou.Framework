using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that draws its inputs over each other, the first at the bottom: <c>feMerge</c> of SVG.</summary>
/// <remarks>Each input is drawn over the result of those before it with the operator over: <c>r = i + r (1 - ai)</c>, on colors multiplied by their alpha.</remarks>
public sealed class MergePrimitive : FilterPrimitive
{
    private readonly FilterInput[] _inputs;

    /// <summary>Initializes a new instance of the <see cref="MergePrimitive"/> class.</summary>
    /// <param name="inputs">The inputs, from the bottom to the top: the <c>feMergeNode</c> elements. They are copied.</param>
    public MergePrimitive(params ReadOnlySpan<FilterInput> inputs)
    {
        _inputs = inputs.ToArray();
    }

    /// <summary>Gets the inputs, from the bottom to the top.</summary>
    public IReadOnlyList<FilterInput> Inputs => _inputs;

    internal override int InputCount => _inputs.Length;

    internal override FilterInput GetInput(int index) => _inputs[index];

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var values = result.Values;
        for (var index = 0; index < _inputs.Length; index++)
        {
            var input = context.GetInput(index).Values;
            for (var y = subregion.Top; y < subregion.Bottom; y++)
            {
                if ((y & 63) == 0)
                {
                    context.Processor.CancellationToken.ThrowIfCancellationRequested();
                }

                var offset = result.IndexOf(subregion.Left, y);
                for (var i = offset; i < offset + (4 * subregion.Width); i += 4)
                {
                    double under = 1 - input[i + 3];
                    values[i] = (float)(input[i] + (values[i] * under));
                    values[i + 1] = (float)(input[i + 1] + (values[i + 1] * under));
                    values[i + 2] = (float)(input[i + 2] + (values[i + 2] * under));
                    values[i + 3] = (float)(input[i + 3] + (values[i + 3] * under));
                }
            }
        }
    }
}
