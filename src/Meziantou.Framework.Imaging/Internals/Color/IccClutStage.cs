using System.Buffers.Binary;
using System.Runtime.Intrinsics;

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

    // The number of entries of the table, then the colors counted by Reserve before the entries were decoded, the decoded
    // entries with their charge, and whether the scope refused them (the entries are then read in place until Release)
    private readonly int _entryCount;
    private long _reservedColors;
    private DecodedEntries? _decoded;
    private int _refused;

    // For a multilinear table of 4 inputs, the offset of each vertex of a cell from its first one, in entries: bit i of the
    // vertex number selects the upper grid point of input i
    private readonly int[]? _vertexEntryOffsets;

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

        _entryCount = stride / bytesPerEntry;
        if (multilinear && _inputs == 4)
        {
            _vertexEntryOffsets = new int[16];
            for (var vertex = 0; vertex < 16; vertex++)
            {
                for (var i = 0; i < 4; i++)
                {
                    if ((vertex & (1 << i)) != 0)
                    {
                        _vertexEntryOffsets[vertex] += _strides[i] / bytesPerEntry;
                    }
                }
            }
        }
    }

    /// <summary>Initializes a new instance of the <see cref="IccClutStage"/> class with the table of another stage and none of its decoded entries.</summary>
    private IccClutStage(IccClutStage other)
    {
        _data = other._data;
        _multilinear = other._multilinear;
        _inputs = other._inputs;
        _outputs = other._outputs;
        _bytesPerEntry = other._bytesPerEntry;
        _gridPoints = other._gridPoints;
        _strides = other._strides;
        _entryCount = other._entryCount;
        _vertexEntryOffsets = other._vertexEntryOffsets;
    }

    /// <summary>The largest number of entries that <see cref="Reserve"/> decodes (2 MiB of doubles): a 17x17x17x17 CMYK table to 3 outputs fits.</summary>
    internal const int MaxDecodedEntries = 1 << 18;

    /// <summary>
    /// Counts colors that are about to be interpolated; once they reach an eighth of the number of entries of the table
    /// (decoding an entry costs about as much as dividing it, and a color reads up to 16 entries of 3 outputs), the entries
    /// are divided by 255 or 65535 once, and the interpolations read the quotients instead of dividing every entry they
    /// read: the same values, so the results are unchanged. A table of more than <see cref="MaxDecodedEntries"/> entries
    /// is never decoded.
    /// </summary>
    /// <remarks>
    /// The decoded entries are charged to <paramref name="scope"/> before they are allocated, and stay charged until
    /// <see cref="Release"/>. When the charge would exceed the limit, nothing is charged and the entries are read in place
    /// from the profile, as before they were decoded: the results are the same.
    /// </remarks>
    /// <param name="colors">The number of colors.</param>
    /// <param name="scope">The scope of the owner of the conversion, which calls <see cref="Release"/> once it is done.</param>
    public void Reserve(int colors, AllocationScope scope)
    {
        if (_entryCount > MaxDecodedEntries || Volatile.Read(ref _decoded) is not null || Volatile.Read(ref _refused) != 0)
            return;

        if (Interlocked.Add(ref _reservedColors, colors) < _entryCount / 8)
            return;

        // One more value, so that a vector of 4 values can be read at the last entry (the last lane is ignored)
        var length = _entryCount + 1;
        AllocationCharge? charge = null;
        try
        {
            try
            {
                charge = scope.Charge((long)length * sizeof(double), AllocationKind.Temporary);
            }
            catch (ImageResourceLimitException)
            {
                Volatile.Write(ref _refused, 1);
                return;
            }

            var entries = new double[length];
            var data = _data.Span;
            for (var i = 0; i < _entryCount; i++)
            {
                entries[i] = _bytesPerEntry == 1 ? data[i] / 255.0 : BinaryPrimitives.ReadUInt16BigEndian(data[(2 * i)..]) / 65535.0;
            }

            // Two threads may decode the entries at once: the charge of the one that loses is released
            if (Interlocked.CompareExchange(ref _decoded, new DecodedEntries(entries, charge), comparand: null) is null)
            {
                charge = null;
            }
        }
        finally
        {
            charge?.Dispose();
        }
    }

    /// <summary>Creates a stage of the same table whose entries are decoded on their own (<see cref="IccPipeline.BindLookupTables"/>).</summary>
    public IccClutStage CloneWithoutEntries() => new(this);

    /// <summary>Releases the decoded entries and their charge: the next colors read the entries in place, until <see cref="Reserve"/> decodes them again.</summary>
    public void Release()
    {
        Interlocked.Exchange(ref _decoded, null)?.Charge.Dispose();
        Volatile.Write(ref _refused, 0);
        Interlocked.Exchange(ref _reservedColors, 0);
    }

    public override void Apply(Span<double> values) => Apply(values, isScalar: false);

    /// <summary>The reference of <see cref="Apply(Span{double})"/>: each entry is divided when it is read (<see cref="InterpolateScalar"/>).</summary>
    internal void ApplyScalar(Span<double> values) => Apply(values, isScalar: true);

    private void Apply(Span<double> values, bool isScalar)
    {
        if (!isScalar && _outputs == 3 && _vertexEntryOffsets is not null && Vector128.IsHardwareAccelerated && Volatile.Read(ref _decoded)?.Values is { } entries)
        {
            ApplyMultilinear4(values, entries, _vertexEntryOffsets);
            return;
        }

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

        if (isScalar)
        {
            InterpolateScalar(vertexWeights[..count], vertexOffsets[..count], values);
        }
        else
        {
            Interpolate(vertexWeights[..count], vertexOffsets[..count], values);
        }
    }

    /// <summary>
    /// The <see cref="Apply(Span{double})"/> of a multilinear table of 4 inputs and 3 outputs whose entries are decoded
    /// (<see cref="Reserve"/>): the same weights, each the product of the same factors in the same order (1.0 times the
    /// factor of the first input is that factor), computed for pairs of vertices; and the same sums, in vertex order, of
    /// every vertex: a vertex of weight zero adds a product that is zero to a sum of products that are not negative, which
    /// does not change it, so it is not skipped (a branch that the colors of a photo, often on the faces of the cells,
    /// mispredict).
    /// </summary>
    private void ApplyMultilinear4(Span<double> values, double[] entries, int[] vertexOffsets)
    {
        var gridPoints = _gridPoints;
        var strides = _strides;
        var offset = 0;
        Span<double> fractions = unsafe(stackalloc double[4]);
        for (var i = 0; i < 4; i++)
        {
            var last = gridPoints[i] - 1;
            var position = IccCurve.Clip(values[i]) * last;
            var index = Math.Min((int)position, last - 1);
            fractions[i] = position - index;
            offset += index * strides[i];
        }

        // Pairs of vertices (2k, 2k + 1), which differ by the first input only
        var (f1, f2, f3) = (fractions[1], fractions[2], fractions[3]);
        var (c1, c2, c3) = (1 - f1, 1 - f2, 1 - f3);
        var first = Vector128.Create(1 - fractions[0], fractions[0]);
        var w1Low = first * c1;
        var w1High = first * f1;
        var w2a = w1Low * c2;
        var w2b = w1High * c2;
        var w2c = w1Low * f2;
        var w2d = w1High * f2;
        Span<double> weights = unsafe(stackalloc double[16]);
        (w2a * c3).CopyTo(weights);
        (w2b * c3).CopyTo(weights[2..]);
        (w2c * c3).CopyTo(weights[4..]);
        (w2d * c3).CopyTo(weights[6..]);
        (w2a * f3).CopyTo(weights[8..]);
        (w2b * f3).CopyTo(weights[10..]);
        (w2c * f3).CopyTo(weights[12..]);
        (w2d * f3).CopyTo(weights[14..]);

        var origin = offset / _bytesPerEntry;
        if (Vector256.IsHardwareAccelerated)
        {
            // The fourth lane, the first output of the next entry (or the padding), is not used
            var sums = Vector256<double>.Zero;
            for (var vertex = 0; vertex < 16; vertex++)
            {
                sums += Vector256.Create(weights[vertex]) * Vector256.Create(entries.AsSpan(origin + vertexOffsets[vertex], 4));
            }

            values[0] = sums.GetElement(0);
            values[1] = sums.GetElement(1);
            values[2] = sums.GetElement(2);
            return;
        }

        var pair = Vector128<double>.Zero;
        double sum2 = 0;
        for (var vertex = 0; vertex < 16; vertex++)
        {
            var weight = weights[vertex];
            var entry = entries.AsSpan(origin + vertexOffsets[vertex], 3);
            pair += Vector128.Create(weight) * Vector128.Create(entry);
            sum2 += weight * entry[2];
        }

        values[0] = pair.GetElement(0);
        values[1] = pair.GetElement(1);
        values[2] = sum2;
    }

    /// <summary>
    /// Sets the outputs to the sum, in vertex order, of the entries of the vertices whose weight is not zero times their
    /// weights: <c>0 + w0 e0 + w1 e1 + ...</c> for each output.
    /// </summary>
    /// <remarks>
    /// The three sums of the usual case are the lanes of a vector, read in place from the table: each entry divided by
    /// <c>255</c> or <c>65535</c> in its lane, then the same products and additions in the same order as
    /// <see cref="InterpolateScalar"/>.
    /// </remarks>
    private void Interpolate(ReadOnlySpan<double> weights, ReadOnlySpan<int> offsets, Span<double> values)
    {
        if (Volatile.Read(ref _decoded)?.Values is { } entries)
        {
            InterpolateDecoded(weights, offsets, values, entries);
            return;
        }

        if (_outputs != 3 || !Vector128.IsHardwareAccelerated)
        {
            InterpolateScalar(weights, offsets, values);
            return;
        }

        var data = _data.Span;
        if (!Vector256.IsHardwareAccelerated)
        {
            Interpolate128(weights, offsets, values, data);
            return;
        }

        var sums = Vector256<double>.Zero;
        if (_bytesPerEntry == 1)
        {
            var divisor = Vector256.Create(255.0);
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight == 0)
                    continue;

                var entry = data.Slice(offsets[vertex], 3);
                sums += Vector256.Create(weight) * (Vector256.Create(entry[0], entry[1], entry[2], 0.0) / divisor);
            }
        }
        else
        {
            var divisor = Vector256.Create(65535.0);
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight == 0)
                    continue;

                var entry = data.Slice(offsets[vertex], 6);
                var samples = Vector256.Create(BinaryPrimitives.ReadUInt16BigEndian(entry), BinaryPrimitives.ReadUInt16BigEndian(entry[2..]), BinaryPrimitives.ReadUInt16BigEndian(entry[4..]), 0.0);
                sums += Vector256.Create(weight) * (samples / divisor);
            }
        }

        values[0] = sums.GetElement(0);
        values[1] = sums.GetElement(1);
        values[2] = sums.GetElement(2);
    }

    /// <summary>
    /// The sums of <see cref="Interpolate"/> from the decoded entries (<see cref="Reserve"/>): the same products of the same
    /// quotients, added in the same order.
    /// </summary>
    private void InterpolateDecoded(ReadOnlySpan<double> weights, ReadOnlySpan<int> offsets, Span<double> values, double[] entries)
    {
        var shift = _bytesPerEntry - 1;
        if (_outputs == 3 && Vector256.IsHardwareAccelerated)
        {
            var sums = Vector256<double>.Zero;
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight == 0)
                    continue;

                // The fourth lane, the first output of the next entry (or the padding), is not used
                sums += Vector256.Create(weight) * Vector256.Create(entries.AsSpan(offsets[vertex] >> shift, 4));
            }

            values[0] = sums.GetElement(0);
            values[1] = sums.GetElement(1);
            values[2] = sums.GetElement(2);
            return;
        }

        if (_outputs == 3 && Vector128.IsHardwareAccelerated)
        {
            var sums = Vector128<double>.Zero;
            double sum2 = 0;
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight == 0)
                    continue;

                var entry = entries.AsSpan(offsets[vertex] >> shift, 3);
                sums += Vector128.Create(weight) * Vector128.Create(entry);
                sum2 += weight * entry[2];
            }

            values[0] = sums.GetElement(0);
            values[1] = sums.GetElement(1);
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

            var entry = entries.AsSpan(offsets[vertex] >> shift, _outputs);
            for (var i = 0; i < entry.Length; i++)
            {
                result[i] += weight * entry[i];
            }
        }

        result[.._outputs].CopyTo(values);
    }

    /// <summary>The three sums of <see cref="Interpolate"/>: the first two in the lanes of a vector, the third alone.</summary>
    private void Interpolate128(ReadOnlySpan<double> weights, ReadOnlySpan<int> offsets, Span<double> values, ReadOnlySpan<byte> data)
    {
        var sums = Vector128<double>.Zero;
        double sum2 = 0;
        var scale = _bytesPerEntry == 1 ? 255.0 : 65535.0;
        var divisor = Vector128.Create(scale);
        for (var vertex = 0; vertex < weights.Length; vertex++)
        {
            var weight = weights[vertex];
            if (weight == 0)
                continue;

            double sample0, sample1, sample2;
            if (_bytesPerEntry == 1)
            {
                var entry = data.Slice(offsets[vertex], 3);
                (sample0, sample1, sample2) = (entry[0], entry[1], entry[2]);
            }
            else
            {
                var entry = data.Slice(offsets[vertex], 6);
                (sample0, sample1, sample2) = (BinaryPrimitives.ReadUInt16BigEndian(entry), BinaryPrimitives.ReadUInt16BigEndian(entry[2..]), BinaryPrimitives.ReadUInt16BigEndian(entry[4..]));
            }

            sums += Vector128.Create(weight) * (Vector128.Create(sample0, sample1) / divisor);
            sum2 += weight * (sample2 / scale);
        }

        values[0] = sums.GetElement(0);
        values[1] = sums.GetElement(1);
        values[2] = sum2;
    }

    /// <summary>The reference of <see cref="Interpolate"/>: each entry divided when it is read.</summary>
    private void InterpolateScalar(ReadOnlySpan<double> weights, ReadOnlySpan<int> offsets, Span<double> values)
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

    /// <summary>The entries of the table divided by 255 or 65535, and their charge.</summary>
    private sealed class DecodedEntries(double[] values, AllocationCharge charge)
    {
        public double[] Values { get; } = values;

        public AllocationCharge Charge { get; } = charge;
    }
}
