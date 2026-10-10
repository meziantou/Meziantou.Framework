namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A multidimensional color lookup table of an ICC <c>lut8Type</c>, <c>lut16Type</c>, <c>lutAToBType</c> or
/// <c>lutBToAType</c>: a regular grid over the normalized inputs whose entries are the normalized outputs, stored with
/// the first input varying least rapidly (ICC.1:2022 sections 10.10 to 10.13). The entries are read in place from the
/// profile; nothing is copied.
/// </summary>
/// <remarks>
/// <para>
/// The specification does not define the interpolation between grid points. Two rules are used, both exact on grid
/// points, continuous across cells and exact for a table that is affine in each cell; they are part of the numerical
/// contract of the conversion:
/// </para>
/// <para>
/// Simplex interpolation, when the inputs are device channels or CIEXYZ: the grid cell containing the input is
/// subdivided along its main diagonal (tetrahedral interpolation for three inputs). The inputs are visited from the
/// largest fractional position to the smallest, each step moving to the next vertex of the cell along that input, and
/// the result is the sum of the vertex entries weighted by the differences between consecutive fractions (at most five
/// entries for four inputs). Colors of equal inputs, the neutral axis of a device, are interpolated between entries of
/// equal inputs only.
/// </para>
/// <para>
/// Multilinear interpolation, when the inputs are CIELAB: the neutral axis of CIELAB is parallel to the lightness axis,
/// not to the diagonal of the cell, so no diagonal is favored and the eight vertices of the cell are weighted by the
/// products of the fractional positions.
/// </para>
/// </remarks>
internal sealed class IccClutStage : IccStage
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _inputs;
    private readonly int _outputs;
    private readonly int _bytesPerEntry;
    private readonly bool _multilinear;

    // Grid points and entry stride (in table entries of all outputs) of each input
    private readonly int[] _gridPoints;
    private readonly int[] _strides;

    /// <summary>Initializes a new instance of the <see cref="IccClutStage"/> class.</summary>
    /// <param name="data">The table: for each grid point, <paramref name="outputs"/> entries of <paramref name="bytesPerEntry"/> bytes. Its length is validated by the caller.</param>
    /// <param name="gridPoints">The number of grid points of each input (at least two).</param>
    /// <param name="outputs">The number of outputs.</param>
    /// <param name="bytesPerEntry">1 for 8-bit entries, 2 for big-endian 16-bit entries.</param>
    /// <param name="multilinear">Whether the table is interpolated multilinearly (CIELAB inputs) instead of on a simplex.</param>
    public IccClutStage(ReadOnlyMemory<byte> data, ReadOnlySpan<int> gridPoints, int outputs, int bytesPerEntry, bool multilinear)
    {
        _data = data;
        _multilinear = multilinear;
        _inputs = gridPoints.Length;
        _outputs = outputs;
        _bytesPerEntry = bytesPerEntry;
        _gridPoints = gridPoints.ToArray();
        _strides = new int[_inputs];
        var stride = outputs * bytesPerEntry;
        for (var i = _inputs - 1; i >= 0; i--)
        {
            _strides[i] = stride;
            stride *= gridPoints[i];
        }
    }

    public override void Apply(Span<double> values)
    {
        Span<double> fractions = stackalloc double[IccPipeline.MaxChannels];
        Span<int> order = stackalloc int[IccPipeline.MaxChannels];
        Span<double> result = stackalloc double[IccPipeline.MaxChannels];
        result.Clear();

        // The cell containing the input and the position inside it. The last grid point belongs to the last cell
        var offset = 0;
        for (var i = 0; i < _inputs; i++)
        {
            var last = _gridPoints[i] - 1;
            var position = IccCurve.Clip(values[i]) * last;
            var index = Math.Min((int)position, last - 1);
            fractions[i] = position - index;
            offset += index * _strides[i];

            // Insertion sort of the inputs by decreasing fraction
            var slot = i;
            while (slot > 0 && fractions[order[slot - 1]] < fractions[i])
            {
                order[slot] = order[slot - 1];
                slot--;
            }

            order[slot] = i;
        }

        var data = _data.Span;
        if (_multilinear)
        {
            // Every vertex of the cell: bit i of the vertex number selects the upper grid point of input i
            for (var vertex = 0; vertex < 1 << _inputs; vertex++)
            {
                var vertexWeight = 1.0;
                var vertexOffset = offset;
                for (var i = 0; i < _inputs; i++)
                {
                    if ((vertex & (1 << i)) != 0)
                    {
                        vertexWeight *= fractions[i];
                        vertexOffset += _strides[i];
                    }
                    else
                    {
                        vertexWeight *= 1 - fractions[i];
                    }
                }

                if (vertexWeight != 0)
                {
                    Accumulate(data[vertexOffset..], vertexWeight, result);
                }
            }

            result[.._outputs].CopyTo(values);
            return;
        }

        var weight = 1 - fractions[order[0]];
        for (var vertex = 0; ; vertex++)
        {
            if (weight != 0)
            {
                Accumulate(data[offset..], weight, result);
            }

            if (vertex == _inputs)
                break;

            var input = order[vertex];
            offset += _strides[input];
            weight = fractions[input] - (vertex + 1 < _inputs ? fractions[order[vertex + 1]] : 0);
        }

        result[.._outputs].CopyTo(values);
    }

    private void Accumulate(ReadOnlySpan<byte> entry, double weight, Span<double> result)
    {
        if (_bytesPerEntry == 1)
        {
            for (var i = 0; i < _outputs; i++)
            {
                result[i] += weight * (entry[i] / 255.0);
            }
        }
        else
        {
            for (var i = 0; i < _outputs; i++)
            {
                result[i] += weight * (IccReader.ReadUInt16(entry[(2 * i)..]) / 65535.0);
            }
        }
    }
}
