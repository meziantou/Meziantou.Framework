using System.Buffers.Binary;
using System.Runtime.CompilerServices;

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
/// Simplex interpolation, when the inputs are one or three device channels (gray, RGB) or CIEXYZ: the grid cell
/// containing the input is subdivided along its main diagonal (tetrahedral interpolation for three inputs). The inputs
/// are visited from the largest fractional position to the smallest, each step moving to the next vertex of the cell
/// along that input, and the result is the sum of the vertex entries weighted by the differences between consecutive
/// fractions. Colors of equal inputs, the neutral axis of such a space, are interpolated between entries of equal
/// inputs only.
/// </para>
/// <para>
/// Multilinear interpolation, when the inputs are CIELAB or four device channels (CMYK): the neutral axis of these
/// spaces is not the diagonal of the cell (it is parallel to the lightness axis in CIELAB, and depends on the black
/// generation in CMYK), so no diagonal is favored and every vertex of the cell is weighted by the product of the
/// fractional positions (8 vertices for three inputs, 16 for four).
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
    /// <param name="multilinear">Whether the table is interpolated multilinearly (CIELAB or CMYK inputs) instead of on a simplex.</param>
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

    // Every element of the stack buffers is written before it is read: the fraction of each input, the order of the inputs
    // (read by the simplex interpolation only, which sorts them), the weights and offsets of the interpolated vertices
    [SkipLocalsInit]
    public override void Apply(Span<double> values)
    {
        Span<double> fractions = unsafe(stackalloc double[IccPipeline.MaxChannels]);
        Span<int> order = unsafe(stackalloc int[IccPipeline.MaxChannels]);

        // The cell containing the input and the position inside it. The last grid point belongs to the last cell
        var gridPoints = _gridPoints;
        var strides = _strides;
        var inputs = _inputs;
        var offset = 0;
        for (var i = 0; i < inputs; i++)
        {
            var last = gridPoints[i] - 1;
            var position = IccCurve.Clip(values[i]) * last;
            var index = Math.Min((int)position, last - 1);
            fractions[i] = position - index;
            offset += index * strides[i];
            if (_multilinear)
                continue;

            // Insertion sort of the inputs by decreasing fraction (simplex interpolation)
            var slot = i;
            while (slot > 0 && fractions[order[slot - 1]] < fractions[i])
            {
                order[slot] = order[slot - 1];
                slot--;
            }

            order[slot] = i;
        }

        // The vertices of the cell that are interpolated, with their weights, in the order of the sums
        Span<double> vertexWeights = unsafe(stackalloc double[1 << IccPipeline.MaxChannels]);
        Span<int> vertexOffsets = unsafe(stackalloc int[1 << IccPipeline.MaxChannels]);
        int count;
        if (_multilinear)
        {
            // Every vertex of the cell: bit i of the vertex number selects the upper grid point of input i. Its weight is
            // 1.0 times, input by input in increasing order, the fraction (upper grid point) or 1 minus the fraction: the
            // vertices that agree on the first inputs share the products of those inputs, computed once (the same
            // operations in the same order)
            vertexWeights[0] = 1.0;
            vertexOffsets[0] = offset;
            count = 1;
            for (var i = 0; i < inputs; i++)
            {
                var fraction = fractions[i];
                var complement = 1 - fraction;
                var stride = strides[i];
                var upperWeights = vertexWeights.Slice(count, count);
                var upperOffsets = vertexOffsets.Slice(count, count);
                for (var vertex = 0; vertex < upperWeights.Length; vertex++)
                {
                    var vertexWeight = vertexWeights[vertex];
                    upperWeights[vertex] = vertexWeight * fraction;
                    upperOffsets[vertex] = vertexOffsets[vertex] + stride;
                    vertexWeights[vertex] = vertexWeight * complement;
                }

                count *= 2;
            }
        }
        else
        {
            var weight = 1 - fractions[order[0]];
            for (var vertex = 0; ; vertex++)
            {
                vertexWeights[vertex] = weight;
                vertexOffsets[vertex] = offset;
                if (vertex == _inputs)
                    break;

                var input = order[vertex];
                offset += _strides[input];
                weight = fractions[input] - (vertex + 1 < _inputs ? fractions[order[vertex + 1]] : 0);
            }

            count = _inputs + 1;
        }

        Interpolate(vertexWeights[..count], vertexOffsets[..count], values);
    }

    /// <summary>
    /// Sets the outputs to the sum, in vertex order, of the entries of the vertices whose weight is not zero times their
    /// weights: <c>0 + w0 e0 + w1 e1 + ...</c> for each output.
    /// </summary>
    private void Interpolate(ReadOnlySpan<double> weights, ReadOnlySpan<int> offsets, Span<double> values)
    {
        var data = _data.Span;
        if (_outputs == 3)
        {
            // The usual case (CIELAB, CIEXYZ or RGB outputs): the sums stay in registers
            double sum0 = 0, sum1 = 0, sum2 = 0;
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight == 0)
                    continue;

                if (_bytesPerEntry == 1)
                {
                    var entry = data.Slice(offsets[vertex], 3);
                    sum0 += weight * (entry[0] / 255.0);
                    sum1 += weight * (entry[1] / 255.0);
                    sum2 += weight * (entry[2] / 255.0);
                }
                else
                {
                    var entry = data.Slice(offsets[vertex], 6);
                    sum0 += weight * (BinaryPrimitives.ReadUInt16BigEndian(entry) / 65535.0);
                    sum1 += weight * (BinaryPrimitives.ReadUInt16BigEndian(entry[2..]) / 65535.0);
                    sum2 += weight * (BinaryPrimitives.ReadUInt16BigEndian(entry[4..]) / 65535.0);
                }
            }

            values[0] = sum0;
            values[1] = sum1;
            values[2] = sum2;
            return;
        }

        Span<double> result = unsafe(stackalloc double[IccPipeline.MaxChannels]);
        result.Clear();
        for (var vertex = 0; vertex < weights.Length; vertex++)
        {
            var weight = weights[vertex];
            if (weight == 0)
                continue;

            var entry = data.Slice(offsets[vertex], _outputs * _bytesPerEntry);
            for (var i = 0; i < _outputs; i++)
            {
                result[i] += weight * (_bytesPerEntry == 1 ? entry[i] / 255.0 : BinaryPrimitives.ReadUInt16BigEndian(entry[(2 * i)..]) / 65535.0);
            }
        }

        result[.._outputs].CopyTo(values);
    }
}
