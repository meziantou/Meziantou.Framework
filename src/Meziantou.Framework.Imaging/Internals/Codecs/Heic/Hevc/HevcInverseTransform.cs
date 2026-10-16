using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The transformation process for scaled transform coefficients (Rec. ITU-T H.265, clause 8.6.4): the inverse of the
/// integer approximations of the discrete cosine transform of 4 to 32 samples, and of the discrete sine transform of 4
/// samples. Every operation is an integer operation: the result is the one of the specification, bit for bit.
/// The vector path computes 4 outputs at a time with the 32-bit arithmetic of its scalar reference
/// (<see cref="TransformScalar"/>), which it equals bit for bit.
/// </summary>
internal static class HevcInverseTransform
{
    // The matrices of equations 8-315 and 8-317 as 32-bit values, for the vector loads
    private static readonly int[] DstValues = ToInt32(HevcTransformTables.DstMatrix);
    private static readonly int[] DctValues = ToInt32(HevcTransformTables.TransMatrix);

    /// <summary>The smallest value of a transform coefficient and of an intermediate sample: <c>CoeffMinY</c> and <c>CoeffMinC</c> without extended precision.</summary>
    public const int CoeffMin = -32768;

    /// <summary>The largest value of a transform coefficient and of an intermediate sample: <c>CoeffMaxY</c> and <c>CoeffMaxC</c>.</summary>
    public const int CoeffMax = 32767;

    /// <summary>
    /// Transforms the scaled transform coefficients <c>d</c> of a block into the residual samples <c>r</c>, before their
    /// rounding to the bit depth (clause 8.6.4.1 "General").
    /// </summary>
    /// <param name="block">The rows of the block, <c>nTbS</c> values each: the coefficients, replaced by the residual samples.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="scratch">A scratch of <c>nTbS * nTbS</c> values.</param>
    public static void Transform(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch)
    {
        var nTbS = 1 << log2TrafoSize;
        Transform(block, log2TrafoSize, trType, scratch, nTbS, nTbS, residualShift: 0);
    }

    /// <summary>
    /// Transforms the scaled transform coefficients <c>d</c> of a block whose coefficients that are not zero are in its
    /// first rows and columns into the residual samples <c>r</c>, rounded by Equation 8-299 when
    /// <paramref name="residualShift"/> is not zero: <c>( r + ( 1 &lt;&lt; ( bdShift - 1 ) ) ) &gt;&gt; bdShift</c>.
    /// </summary>
    /// <param name="block">The rows of the block, <c>nTbS</c> values each: the coefficients, whose values outside the first <paramref name="rows"/> rows and <paramref name="columns"/> columns are not read and count as zeros; replaced by the residual samples.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="scratch">A scratch of <c>nTbS * nTbS</c> values.</param>
    /// <param name="rows">The number of rows that can have coefficients that are not zero: 1 to <c>nTbS</c>.</param>
    /// <param name="columns">The number of columns that can have coefficients that are not zero: a multiple of 4 up to <c>nTbS</c>.</param>
    /// <param name="residualShift"><c>bdShift</c> of Equation 8-299, or 0 for the residual samples before that rounding.</param>
    public static void Transform(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch, int rows, int columns, int residualShift)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            TransformVector(block, log2TrafoSize, trType, scratch, rows, columns, residualShift);
            return;
        }

        // The coefficients outside the rows and the columns are zeros
        var nTbS = 1 << log2TrafoSize;
        for (var y = 0; y < nTbS; y++)
        {
            block.Slice((y * nTbS) + (y < rows ? columns : 0), y < rows ? nTbS - columns : nTbS).Clear();
        }

        TransformScalar(block, log2TrafoSize, trType, scratch);
        if (residualShift > 0)
        {
            var rounding = 1 << (residualShift - 1);
            for (var i = 0; i < nTbS * nTbS; i++)
            {
                block[i] = (block[i] + rounding) >> residualShift;
            }
        }
    }

    /// <summary>The transformation of <see cref="Transform(Span{int}, int, int, Span{int})"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void TransformScalar(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch)
    {
        var nTbS = 1 << log2TrafoSize;
        Span<int> input = stackalloc int[32];
        Span<int> output = stackalloc int[32];

        // 1. Each column of d is transformed to e; 2. g = Clip3( coeffMin, coeffMax, ( e + 64 ) >> 7 )
        for (var x = 0; x < nTbS; x++)
        {
            var any = false;
            for (var y = 0; y < nTbS; y++)
            {
                input[y] = block[(y * nTbS) + x];
                any |= input[y] is not 0;
            }

            if (!any)
            {
                for (var y = 0; y < nTbS; y++)
                {
                    scratch[(y * nTbS) + x] = 0;
                }

                continue;
            }

            Transform1D(input, output, log2TrafoSize, trType);
            for (var y = 0; y < nTbS; y++)
            {
                scratch[(y * nTbS) + x] = Math.Clamp((output[y] + 64) >> 7, CoeffMin, CoeffMax);
            }
        }

        // 3. Each row of g is transformed to r
        for (var y = 0; y < nTbS; y++)
        {
            Transform1D(scratch.Slice(y * nTbS, nTbS), block.Slice(y * nTbS, nTbS), log2TrafoSize, trType);
        }
    }

    // Each output of a one-dimensional transform is a sum of products of the coefficients by a column of the matrix: the
    // column pass computes 4 columns of the block at a time (one output row of 4 columns), the row pass 4 outputs of a row
    // at a time. The coefficients after the last one that is not zero add nothing to the sums, and the columns that have
    // no coefficient give columns of zeros.
    private static void TransformVector(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch, int rows, int columns, int residualShift)
    {
        var nTbS = 1 << log2TrafoSize;
        var size = nTbS * nTbS;
        block = block[..size];
        int lastRow;
        if (columns == nTbS)
        {
            var lastIndex = block[..(rows * nTbS)].LastIndexOfAnyExcept(0);
            lastRow = lastIndex < 0 ? -1 : lastIndex / nTbS;
        }
        else
        {
            lastRow = rows - 1;
            while (lastRow >= 0 && !block.Slice(lastRow * nTbS, columns).ContainsAnyExcept(0))
            {
                lastRow--;
            }
        }

        if (lastRow < 0)
        {
            // A block of zeros transforms to zeros
            block.Clear();
            return;
        }

        ReadOnlySpan<int> matrix;
        int rowStep;
        if (trType is 1)
        {
            matrix = DstValues;
            rowStep = 4;
        }
        else
        {
            // Equation 8-317: the coefficient j is the one of the row j * 2^( 5 - Log2( nTbS ) ) of the matrix
            matrix = DctValues;
            rowStep = 32 << (5 - log2TrafoSize);
        }

        // 1. Each column of d is transformed to e; 2. g = Clip3( coeffMin, coeffMax, ( e + 64 ) >> 7 )
        var g = scratch[..size];
        var round = Vector128.Create(64);
        var min = Vector128.Create(CoeffMin);
        var max = Vector128.Create(CoeffMax);
        for (var y = 0; y < nTbS; y++)
        {
            var output = g.Slice(y * nTbS, nTbS);
            for (var x = 0; x < columns; x += 4)
            {
                var sum = Vector128<int>.Zero;
                for (var j = 0; j <= lastRow; j++)
                {
                    sum += Vector128.Create(block.Slice((j * nTbS) + x, 4)) * matrix[(j * rowStep) + y];
                }

                Vector128.Min(Vector128.Max((sum + round) >> 7, min), max).CopyTo(output[x..]);
            }

            output[columns..].Clear();
        }

        // 3. Each row of g is transformed to r, then rounded by Equation 8-299
        var rounding = Vector128.Create(residualShift > 0 ? 1 << (residualShift - 1) : 0);
        for (var y = 0; y < nTbS; y++)
        {
            var input = g.Slice(y * nTbS, columns);
            var output = block.Slice(y * nTbS, nTbS);
            var last = input.LastIndexOfAnyExcept(0);
            if (last < 0)
            {
                output.Clear();
                continue;
            }

            for (var i = 0; i < nTbS; i += 4)
            {
                var sum = Vector128<int>.Zero;
                for (var j = 0; j <= last; j++)
                {
                    sum += Vector128.Create(matrix.Slice((j * rowStep) + i, 4)) * input[j];
                }

                ((sum + rounding) >> residualShift).CopyTo(output[i..]);
            }
        }
    }

    private static int[] ToInt32(ReadOnlySpan<sbyte> values)
    {
        var result = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = values[i];
        }

        return result;
    }

    /// <summary>
    /// Transforms a list of scaled transform coefficients <c>x</c> into the list of transformed samples <c>y</c> (clause
    /// 8.6.4.2 "Transformation process").
    /// </summary>
    public static void Transform1D(ReadOnlySpan<int> x, Span<int> y, int log2TrafoSize, int trType)
    {
        var nTbS = 1 << log2TrafoSize;
        if (trType is 1)
        {
            // Equation 8-315
            var transMatrix = HevcTransformTables.DstMatrix;
            for (var i = 0; i < 4; i++)
            {
                var sum = 0;
                for (var j = 0; j < 4; j++)
                {
                    sum += transMatrix[(j * 4) + i] * x[j];
                }

                y[i] = sum;
            }

            return;
        }

        // Equation 8-317: the coefficient j is the one of the row j * 2^( 5 - Log2( nTbS ) ) of the matrix
        var matrix = HevcTransformTables.TransMatrix;
        var rowStep = 32 << (5 - log2TrafoSize);
        var last = nTbS - 1;
        while (last > 0 && x[last] is 0)
        {
            last--;
        }

        for (var i = 0; i < nTbS; i++)
        {
            var sum = 0;
            for (var j = 0; j <= last; j++)
            {
                sum += matrix[(j * rowStep) + i] * x[j];
            }

            y[i] = sum;
        }
    }
}
