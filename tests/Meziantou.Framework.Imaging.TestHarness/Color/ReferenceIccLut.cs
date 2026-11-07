using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// The lookup-table tag types of ICC profiles in <see cref="decimal"/> arithmetic, evaluated element by element on
/// normalized values: <c>lut8Type</c> and <c>lut16Type</c> (ICC.1:2022 sections 10.10 and 10.11), <c>lutAToBType</c> and
/// <c>lutBToAType</c> (sections 10.12 and 10.13).
/// </summary>
/// <remarks>
/// The specification does not define the interpolation of the color lookup table; the reference follows the rules
/// documented by the library. One or three device channels and CIEXYZ inputs: the simplex of the grid cell containing
/// the input, for the subdivision of the cell along its main diagonal; the simplex is found here by trying every ordering
/// of the inputs and keeping the one whose barycentric coordinates are all non-negative, instead of sorting. CIELAB
/// inputs and four device channels: multilinear interpolation, written here as successive linear interpolations along
/// each input.
/// </remarks>
internal static class ReferenceIccLut
{
    /// <summary>Evaluates a lookup-table tag.</summary>
    /// <param name="tag">The tag data.</param>
    /// <param name="input">The normalized inputs.</param>
    /// <param name="deviceToConnection">Whether the tag is an AToB tag.</param>
    /// <param name="connectionIsXyz">Whether the profile connection space is CIEXYZ.</param>
    /// <returns>The normalized outputs.</returns>
    public static decimal[] Evaluate(ReadOnlySpan<byte> tag, decimal[] input, bool deviceToConnection, bool connectionIsXyz)
    {
        var type = Encoding.ASCII.GetString(tag[..4]);

        // Multilinear when the table input is CIELAB (a BToA table of a CIELAB profile) or has four channels (CMYK)
        var multilinear = (!deviceToConnection && !connectionIsXyz) || input.Length == 4;
        return type switch
        {
            "mft1" => EvaluateLut(tag, input, bytesPerEntry: 1, useMatrix: !deviceToConnection && connectionIsXyz, multilinear),
            "mft2" => EvaluateLut(tag, input, bytesPerEntry: 2, useMatrix: !deviceToConnection && connectionIsXyz, multilinear),
            "mAB " when deviceToConnection => EvaluateLutAToB(tag, input, deviceToConnection: true, multilinear),
            "mBA " when !deviceToConnection => EvaluateLutAToB(tag, input, deviceToConnection: false, multilinear),
            _ => throw new NotSupportedException($"Lookup table type '{type}' is not supported in this direction."),
        };
    }

    private static decimal[] EvaluateLut(ReadOnlySpan<byte> tag, decimal[] input, int bytesPerEntry, bool useMatrix, bool multilinear)
    {
        int inputs = tag[8];
        int outputs = tag[9];
        int gridPoints = tag[10];
        var maximum = bytesPerEntry == 1 ? 255m : 65535m;
        var values = (decimal[])input.Clone();
        if (useMatrix)
        {
            var matrix = new decimal[9];
            for (var i = 0; i < 9; i++)
            {
                matrix[i] = BinaryPrimitives.ReadInt32BigEndian(tag[(12 + (4 * i))..]) / 65536m;
            }

            values = ReferenceIccMath.Multiply(matrix, values);
        }

        var position = 48;
        var inputEntries = 256;
        var outputEntries = 256;
        if (bytesPerEntry == 2)
        {
            inputEntries = BinaryPrimitives.ReadUInt16BigEndian(tag[48..]);
            outputEntries = BinaryPrimitives.ReadUInt16BigEndian(tag[50..]);
            position = 52;
        }

        for (var channel = 0; channel < inputs; channel++)
        {
            values[channel] = ReferenceIccCurve.FromTable(ReadEntries(tag[position..], inputEntries, bytesPerEntry, maximum)).Evaluate(values[channel]);
            position += inputEntries * bytesPerEntry;
        }

        var grid = Enumerable.Repeat(gridPoints, inputs).ToArray();
        values = Interpolate(tag[position..], grid, outputs, bytesPerEntry, values, multilinear);
        position += checked((int)(Pow(gridPoints, inputs) * outputs * bytesPerEntry));
        for (var channel = 0; channel < outputs; channel++)
        {
            values[channel] = ReferenceIccCurve.FromTable(ReadEntries(tag[position..], outputEntries, bytesPerEntry, maximum)).Evaluate(values[channel]);
            position += outputEntries * bytesPerEntry;
        }

        return values;
    }

    private static decimal[] EvaluateLutAToB(ReadOnlySpan<byte> tag, decimal[] input, bool deviceToConnection, bool multilinear)
    {
        int inputs = tag[8];
        int outputs = tag[9];
        var offsetB = BinaryPrimitives.ReadInt32BigEndian(tag[12..]);
        var offsetMatrix = BinaryPrimitives.ReadInt32BigEndian(tag[16..]);
        var offsetM = BinaryPrimitives.ReadInt32BigEndian(tag[20..]);
        var offsetClut = BinaryPrimitives.ReadInt32BigEndian(tag[24..]);
        var offsetA = BinaryPrimitives.ReadInt32BigEndian(tag[28..]);
        var values = (decimal[])input.Clone();

        // AToB: A curves, color lookup table, M curves, matrix, B curves. BToA: the reverse order
        string[] order = deviceToConnection ? ["A", "CLUT", "M", "matrix", "B"] : ["B", "matrix", "M", "CLUT", "A"];
        foreach (var element in order)
        {
            switch (element)
            {
                case "A" when offsetA != 0:
                    values = ApplyCurves(tag[offsetA..], values);
                    break;

                case "M" when offsetM != 0:
                    values = ApplyCurves(tag[offsetM..], values);
                    break;

                case "B" when offsetB != 0:
                    values = ApplyCurves(tag[offsetB..], values);
                    break;

                case "matrix" when offsetMatrix != 0:
                    var matrix = new decimal[12];
                    for (var i = 0; i < 12; i++)
                    {
                        matrix[i] = BinaryPrimitives.ReadInt32BigEndian(tag[(offsetMatrix + (4 * i))..]) / 65536m;
                    }

                    // Section 10.12.5: the result is clipped to [0, 1]
                    values = ReferenceIccMath.Multiply(matrix[..9], values);
                    for (var i = 0; i < 3; i++)
                    {
                        values[i] = ReferenceIccMath.Clip(values[i] + matrix[9 + i]);
                    }

                    break;

                case "CLUT" when offsetClut != 0:
                    var header = tag.Slice(offsetClut, 20);
                    var grid = new int[inputs];
                    for (var i = 0; i < inputs; i++)
                    {
                        grid[i] = header[i];
                    }

                    values = Interpolate(tag[(offsetClut + 20)..], grid, outputs, header[16], values, multilinear);
                    break;
            }
        }

        return values;
    }

    private static decimal[] ApplyCurves(ReadOnlySpan<byte> data, decimal[] values)
    {
        var result = new decimal[values.Length];
        var position = 0;
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = ReferenceIccCurve.Parse(data[position..], out var length).Evaluate(values[i]);
            position += (length + 3) / 4 * 4;
        }

        return result;
    }

    private static decimal[] ReadEntries(ReadOnlySpan<byte> data, int count, int bytesPerEntry, decimal maximum)
    {
        var entries = new decimal[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = (bytesPerEntry == 1 ? data[i] : BinaryPrimitives.ReadUInt16BigEndian(data[(2 * i)..])) / maximum;
        }

        return entries;
    }

    /// <summary>Interpolates the table: the first input varies least rapidly.</summary>
    private static decimal[] Interpolate(ReadOnlySpan<byte> table, int[] grid, int outputs, int bytesPerEntry, decimal[] input, bool multilinear)
    {
        var inputs = grid.Length;
        var cell = new int[inputs];
        var fraction = new decimal[inputs];
        for (var i = 0; i < inputs; i++)
        {
            var position = ReferenceIccMath.Clip(input[i]) * (grid[i] - 1);
            cell[i] = Math.Min((int)decimal.Floor(position), grid[i] - 2);
            fraction[i] = position - cell[i];
        }

        if (multilinear)
            return InterpolateMultilinear(table, grid, outputs, bytesPerEntry, cell, fraction, dimension: 0);

        foreach (var permutation in Permutations(inputs))
        {
            // Vertices: the cell origin, then one more input at its upper grid point at each step. The barycentric
            // coordinates of the input in this simplex are 1 - f(p0), f(p0) - f(p1), ..., f(p(n-1))
            var weights = new decimal[inputs + 1];
            weights[0] = 1 - fraction[permutation[0]];
            for (var k = 1; k <= inputs; k++)
            {
                weights[k] = fraction[permutation[k - 1]] - (k < inputs ? fraction[permutation[k]] : 0);
            }

            if (weights.Any(weight => weight < 0))
                continue;

            var result = new decimal[outputs];
            var vertex = (int[])cell.Clone();
            for (var k = 0; k <= inputs; k++)
            {
                if (k > 0)
                {
                    vertex[permutation[k - 1]]++;
                }

                long index = 0;
                for (var i = 0; i < inputs; i++)
                {
                    index = (index * grid[i]) + vertex[i];
                }

                var entry = table[checked((int)(index * outputs * bytesPerEntry))..];
                for (var output = 0; output < outputs; output++)
                {
                    var stored = bytesPerEntry == 1 ? entry[output] / 255m : BinaryPrimitives.ReadUInt16BigEndian(entry[(2 * output)..]) / 65535m;
                    result[output] += weights[k] * stored;
                }
            }

            return result;
        }

        throw new InvalidOperationException("No simplex contains the input.");
    }

    /// <summary>
    /// Interpolates linearly along one input between the two multilinear interpolations of the remaining inputs, at the
    /// lower and the upper grid point of the cell.
    /// </summary>
    private static decimal[] InterpolateMultilinear(ReadOnlySpan<byte> table, int[] grid, int outputs, int bytesPerEntry, int[] vertex, decimal[] fraction, int dimension)
    {
        if (dimension == grid.Length)
        {
            long index = 0;
            for (var i = 0; i < grid.Length; i++)
            {
                index = (index * grid[i]) + vertex[i];
            }

            var entry = table[checked((int)(index * outputs * bytesPerEntry))..];
            var stored = new decimal[outputs];
            for (var output = 0; output < outputs; output++)
            {
                stored[output] = bytesPerEntry == 1 ? entry[output] / 255m : BinaryPrimitives.ReadUInt16BigEndian(entry[(2 * output)..]) / 65535m;
            }

            return stored;
        }

        var lower = InterpolateMultilinear(table, grid, outputs, bytesPerEntry, vertex, fraction, dimension + 1);
        var upperVertex = (int[])vertex.Clone();
        upperVertex[dimension]++;
        var upper = InterpolateMultilinear(table, grid, outputs, bytesPerEntry, upperVertex, fraction, dimension + 1);
        var result = new decimal[outputs];
        for (var output = 0; output < outputs; output++)
        {
            result[output] = lower[output] + ((upper[output] - lower[output]) * fraction[dimension]);
        }

        return result;
    }

    private static IEnumerable<int[]> Permutations(int count)
    {
        if (count == 1)
        {
            yield return [0];
            yield break;
        }

        foreach (var shorter in Permutations(count - 1))
        {
            for (var position = 0; position < count; position++)
            {
                var permutation = new int[count];
                Array.Copy(shorter, 0, permutation, 0, position);
                permutation[position] = count - 1;
                Array.Copy(shorter, position, permutation, position + 1, count - 1 - position);
                yield return permutation;
            }
        }
    }

    private static long Pow(int value, int exponent)
    {
        long result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }
}
