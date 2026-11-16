namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color lookup table element: a grid of float32Number entries read in place, its inputs clipped to [0, 1] (ICC.2:2023
/// section 11.2.3, ICC.1:2022 section 10.16.2.4). The interpolation is multilinear (every vertex of the cell), or on the
/// simplices along the main diagonal of the cell when the interpolation hint asks for tetrahedral interpolation.
/// </summary>
internal sealed class IccMpeClut : IccMpeElement
{
    /// <summary>The most inputs: the 16 bytes of grid points.</summary>
    private const int MaxInputs = 16;

    private readonly ReadOnlyMemory<byte> _entries;
    private readonly int[] _gridPoints;
    private readonly int[] _strides;
    private readonly bool _simplex;

    private IccMpeClut(ReadOnlyMemory<byte> entries, int[] gridPoints, int outputs, bool simplex)
        : base(gridPoints.Length, outputs)
    {
        _entries = entries;
        _gridPoints = gridPoints;
        _simplex = simplex;
        _strides = new int[gridPoints.Length];
        var stride = outputs;
        for (var i = gridPoints.Length - 1; i >= 0; i--)
        {
            _strides[i] = stride;
            stride *= gridPoints[i];
        }
    }

    // The fractions, the cell origin and the order of the inputs
    public override int ScratchSize => 3 * InputCount;

    public override long Cost => _simplex ? (long)(InputCount + 1) * OutputCount * 2 : (1L << InputCount) * OutputCount * 2;

    public static IccMpeClut Parse(ReadOnlyMemory<byte> element, int inputs, int outputs, IccMpeParseContext context)
    {
        var span = element.Span;
        if (inputs > MaxInputs || span.Length < 28)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a color lookup table element has {inputs} inputs, more than its {MaxInputs} grid sizes, or no grid."));

        var gridPoints = new int[inputs];
        long entries = outputs;
        for (var i = 0; i < inputs; i++)
        {
            gridPoints[i] = span[12 + i];
            if (gridPoints[i] < 2)
                throw context.Invalid("a dimension of a color lookup table element has fewer than two grid points.");

            entries *= gridPoints[i];
            if (28 + (4 * entries) > span.Length)
                throw context.Invalid("the entries of a color lookup table element do not fit in it.");
        }

        if ((1L << inputs) * outputs > IccMpeParseContext.MaxCost)
            throw context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"a color lookup table element of {inputs} inputs is too costly to interpolate."), "ICC processing elements too costly to evaluate");

        // The interpolation hint (ICC.2:2023 section 4.2.5) is byte 7, reserved (zero) in ICC.1:2022
        return new IccMpeClut(element.Slice(28, (int)(4 * entries)), gridPoints, outputs, simplex: span[7] == 2);
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch)
    {
        var inputs = InputCount;
        var fractions = scratch[..inputs];
        var origin = scratch.Slice(inputs, inputs);
        var order = scratch.Slice(2 * inputs, inputs);
        var baseIndex = 0;
        for (var i = 0; i < inputs; i++)
        {
            // The input is clipped to [0, 1]; the last grid point belongs to the last cell
            var value = input[i];
            var position = (double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1)) * (_gridPoints[i] - 1);
            var cell = Math.Min((int)position, _gridPoints[i] - 2);
            fractions[i] = position - cell;
            origin[i] = cell;
            baseIndex += cell * _strides[i];
        }

        var entries = _entries.Span;
        output.Clear();
        if (_simplex)
        {
            // From the origin of the cell, move along the inputs from the largest fraction to the smallest
            for (var i = 0; i < inputs; i++)
            {
                order[i] = i;
            }

            for (var i = 1; i < inputs; i++)
            {
                var current = (int)order[i];
                var j = i - 1;
                while (j >= 0 && fractions[(int)order[j]] < fractions[current])
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = current;
            }

            var index = baseIndex;
            var previous = 1.0;
            for (var step = 0; step <= inputs; step++)
            {
                var fraction = step == inputs ? 0 : fractions[(int)order[step]];
                Accumulate(entries, index, previous - fraction, output);
                if (step < inputs)
                {
                    index += _strides[(int)order[step]];
                }

                previous = fraction;
            }

            return;
        }

        for (var vertex = 0; vertex < 1 << inputs; vertex++)
        {
            var weight = 1.0;
            var index = baseIndex;
            for (var i = 0; i < inputs; i++)
            {
                if ((vertex & (1 << i)) != 0)
                {
                    weight *= fractions[i];
                    index += _strides[i];
                }
                else
                {
                    weight *= 1 - fractions[i];
                }
            }

            if (weight != 0)
            {
                Accumulate(entries, index, weight, output);
            }
        }
    }

    private static void Accumulate(ReadOnlySpan<byte> entries, int index, double weight, Span<double> output)
    {
        for (var i = 0; i < output.Length; i++)
        {
            output[i] += weight * System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(entries[(4 * (index + i))..]);
        }
    }
}
