using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The matrix element: Q rows of P coefficients, then Q offsets (ICC.2:2023 section 11.2.10).</summary>
internal sealed class IccMpeMatrix : IccMpeElement
{
    private readonly double[] _coefficients;

    private IccMpeMatrix(int inputs, int outputs, double[] coefficients)
        : base(inputs, outputs)
    {
        _coefficients = coefficients;
    }

    public override long Cost => (long)(InputCount + 1) * OutputCount;

    public static IccMpeMatrix Parse(ReadOnlySpan<byte> element, int inputs, int outputs, IccMpeParseContext context)
    {
        var count = (long)(inputs + 1) * outputs;
        if (12 + (4 * count) > element.Length)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the matrix element of {inputs} inputs and {outputs} outputs needs {count} numbers, which do not fit in its {element.Length} bytes."));

        context.Allocate(count);
        var coefficients = new double[count];
        for (var i = 0; i < coefficients.Length; i++)
        {
            coefficients[i] = IccMpeParser.ReadFiniteSingle(element[(12 + (4 * i))..], context, "matrix coefficient");
        }

        return new IccMpeMatrix(inputs, outputs, coefficients);
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch)
    {
        var inputs = InputCount;
        var offsets = inputs * OutputCount;
        for (var row = 0; row < output.Length; row++)
        {
            var sum = 0.0;
            for (var column = 0; column < inputs; column++)
            {
                sum += _coefficients[(row * inputs) + column] * input[column];
            }

            output[row] = sum + _coefficients[offsets + row];
        }
    }
}
