namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The 3x3 matrices that relate the linear samples of an RGB color space, given by the chromaticities of its primaries
/// and of its white point, to CIE XYZ, and the linear Bradford chromatic adaptation between two white points. Matrices
/// are nine values, row by row.
/// </summary>
internal static class ChromaticityMath
{
    /// <summary>Computes the matrix from linear RGB samples to CIE XYZ: its columns are the primaries, scaled so that equal samples give the white point with a luminance of 1.</summary>
    /// <param name="primaries">The chromaticities of the primaries: the x and y of red, green and blue.</param>
    /// <param name="whiteX">The x of the white point.</param>
    /// <param name="whiteY">The y of the white point.</param>
    /// <param name="matrix">Receives the matrix.</param>
    /// <returns><see langword="false"/> when the chromaticities do not define a color space.</returns>
    public static bool TryGetRgbToXyz(ReadOnlySpan<double> primaries, double whiteX, double whiteY, Span<double> matrix)
    {
        if (!(whiteX >= 0 && whiteX <= 1 && whiteY > 0 && whiteY <= 1))
            return false;

        Span<double> chromaticities =
        [
            primaries[0], primaries[2], primaries[4],
            primaries[1], primaries[3], primaries[5],
            1 - primaries[0] - primaries[1], 1 - primaries[2] - primaries[3], 1 - primaries[4] - primaries[5],
        ];
        Span<double> inverse = stackalloc double[9];
        if (!TryInvert(chromaticities, inverse))
            return false;

        Span<double> white = [whiteX / whiteY, 1, (1 - whiteX - whiteY) / whiteY];
        for (var column = 0; column < 3; column++)
        {
            var scale = (inverse[column * 3] * white[0]) + (inverse[(column * 3) + 1] * white[1]) + (inverse[(column * 3) + 2] * white[2]);
            if (!double.IsFinite(scale))
                return false;

            for (var row = 0; row < 3; row++)
            {
                matrix[(row * 3) + column] = chromaticities[(row * 3) + column] * scale;
            }
        }

        return true;
    }

    /// <summary>Computes the linear Bradford adaptation from a white point, given by its chromaticity, to another one, given in CIE XYZ.</summary>
    /// <returns><see langword="false"/> when the white point is not valid.</returns>
    public static bool TryGetBradfordAdaptation(double whiteX, double whiteY, ReadOnlySpan<double> targetWhite, Span<double> matrix)
    {
        if (!(whiteX >= 0 && whiteX <= 1 && whiteY > 0 && whiteY <= 1))
            return false;

        ReadOnlySpan<double> bradford = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
        ReadOnlySpan<double> bradfordInverse = [0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912, -0.0085287, 0.0400428, 0.9684867];
        ReadOnlySpan<double> white = [whiteX / whiteY, 1, (1 - whiteX - whiteY) / whiteY];
        Span<double> scaled = stackalloc double[9];
        for (var row = 0; row < 3; row++)
        {
            // The response of each cone type to the two white points
            var cone = (bradford[row * 3] * white[0]) + (bradford[(row * 3) + 1] * white[1]) + (bradford[(row * 3) + 2] * white[2]);
            var targetCone = (bradford[row * 3] * targetWhite[0]) + (bradford[(row * 3) + 1] * targetWhite[1]) + (bradford[(row * 3) + 2] * targetWhite[2]);
            if (cone == 0 || !double.IsFinite(targetCone / cone))
                return false;

            for (var column = 0; column < 3; column++)
            {
                scaled[(row * 3) + column] = bradford[(row * 3) + column] * (targetCone / cone);
            }
        }

        Multiply(bradfordInverse, scaled, matrix);
        return true;
    }

    /// <summary>Multiplies two matrices.</summary>
    public static void Multiply(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                result[(row * 3) + column] = (left[row * 3] * right[column]) + (left[(row * 3) + 1] * right[3 + column]) + (left[(row * 3) + 2] * right[6 + column]);
            }
        }
    }

    /// <summary>Inverts a matrix.</summary>
    /// <returns><see langword="false"/> when the matrix has no inverse.</returns>
    public static bool TryInvert(ReadOnlySpan<double> m, Span<double> result)
    {
        var determinant = (m[0] * ((m[4] * m[8]) - (m[5] * m[7]))) - (m[1] * ((m[3] * m[8]) - (m[5] * m[6]))) + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        if (!(Math.Abs(determinant) > 1e-12) || !double.IsFinite(determinant))
            return false;

        var inverse = 1 / determinant;
        result[0] = ((m[4] * m[8]) - (m[5] * m[7])) * inverse;
        result[1] = ((m[2] * m[7]) - (m[1] * m[8])) * inverse;
        result[2] = ((m[1] * m[5]) - (m[2] * m[4])) * inverse;
        result[3] = ((m[5] * m[6]) - (m[3] * m[8])) * inverse;
        result[4] = ((m[0] * m[8]) - (m[2] * m[6])) * inverse;
        result[5] = ((m[2] * m[3]) - (m[0] * m[5])) * inverse;
        result[6] = ((m[3] * m[7]) - (m[4] * m[6])) * inverse;
        result[7] = ((m[1] * m[6]) - (m[0] * m[7])) * inverse;
        result[8] = ((m[0] * m[4]) - (m[1] * m[3])) * inverse;
        return true;
    }
}
