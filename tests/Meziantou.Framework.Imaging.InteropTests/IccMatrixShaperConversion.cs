using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Converts samples from a matrix-based (or monochrome) profile to a linear one with the CIEXYZ connection space:
/// the tone curves of the source, its colorants, then the inverse of the colorants of the destination (ICC.1:2022
/// sections 8.3.3 and 8.3.4). The curves are evaluated at every level of the samples, with their derivatives, which
/// carry the rounding of the samples to the destination.
/// </summary>
internal sealed class IccMatrixShaperConversion
{
    private readonly double[] _matrix;
    private readonly double[] _inverse;
    private readonly double[][] _linear;
    private readonly double[][] _slopes;

    private IccMatrixShaperConversion(double[] matrix, double[][] linear, double[][] slopes)
    {
        _matrix = matrix;
        _inverse = matrix.Length == 1 ? [1 / matrix[0]] : Invert(matrix);
        _linear = linear;
        _slopes = slopes;
    }

    public int ChannelCount => _linear.Length;

    public static IccMatrixShaperConversion Create(byte[] source, byte[] destination, int maxLevel)
    {
        var sourceTags = IccTags.Read(source);
        var destinationTags = IccTags.Read(destination);
        var isGray = sourceTags.ContainsKey("kTRC");
        Assert.Equal(isGray, destinationTags.ContainsKey("kTRC"));
        string[] curves = isGray ? ["kTRC"] : ["rTRC", "gTRC", "bTRC"];

        // The references of the corpus are linear: the curves of the destination are the identity
        foreach (var signature in curves)
        {
            for (var step = 0; step <= 100; step++)
            {
                Assert.True(Math.Abs(IccTags.EvaluateCurve(destinationTags[signature].Span, step / 100.0) - (step / 100.0)) <= 1e-6, $"The {signature} curve of the destination profile is not linear.");
            }
        }

        double[] matrix = [1];
        if (!isGray)
        {
            var toConnectionSpace = ReadColorants(sourceTags);
            var fromConnectionSpace = Invert(ReadColorants(destinationTags));
            matrix = new double[9];
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        matrix[(row * 3) + column] += fromConnectionSpace[(row * 3) + k] * toConnectionSpace[(k * 3) + column];
                    }
                }
            }
        }

        var linear = new double[curves.Length][];
        var slopes = new double[curves.Length][];
        for (var channel = 0; channel < curves.Length; channel++)
        {
            var curve = sourceTags[curves[channel]];
            linear[channel] = new double[maxLevel + 1];
            slopes[channel] = new double[maxLevel + 1];
            for (var level = 0; level <= maxLevel; level++)
            {
                var x = (double)level / maxLevel;
                linear[channel][level] = IccTags.EvaluateCurve(curve.Span, x);
                var low = Math.Max(0, x - (0.5 / maxLevel));
                var high = Math.Min(1, x + (0.5 / maxLevel));
                slopes[channel][level] = Math.Abs(IccTags.EvaluateCurve(curve.Span, high) - IccTags.EvaluateCurve(curve.Span, low)) / (high - low);
            }
        }

        return new IccMatrixShaperConversion(matrix, linear, slopes);
    }

    /// <summary>Converts the color of one pixel.</summary>
    /// <param name="levels">The integer samples of the color channels.</param>
    /// <param name="values">Receives the converted samples (the following channels are left as they are).</param>
    /// <param name="gains">Receives, per channel, the sum of the absolute derivatives: a rounding of half a step moves the sample by up to that times half a step.</param>
    /// <param name="squaredGains">Receives, per channel, the sum of the squared derivatives.</param>
    public void Convert(ReadOnlySpan<int> levels, Span<double> values, Span<double> gains, Span<double> squaredGains)
    {
        var count = _linear.Length;
        for (var row = 0; row < count; row++)
        {
            double value = 0, gain = 0, squaredGain = 0;
            for (var column = 0; column < count; column++)
            {
                var coefficient = _matrix[(row * count) + column];
                value += coefficient * _linear[column][levels[column]];
                var derivative = coefficient * _slopes[column][levels[column]];
                gain += Math.Abs(derivative);
                squaredGain += derivative * derivative;
            }

            values[row] = value;
            gains[row] = gain;
            squaredGains[row] = squaredGain;
        }
    }

    /// <summary>
    /// Determines whether a color of the destination is inside the gamut of the source: its linear values in the source
    /// are in [0, 1], so a decoder that renders it to the source does not clip it.
    /// </summary>
    /// <param name="destination">The linear values of the color in the destination, one per channel.</param>
    public bool IsInSourceGamut(ReadOnlySpan<double> destination)
    {
        var count = _linear.Length;
        for (var row = 0; row < count; row++)
        {
            var value = 0.0;
            for (var column = 0; column < count; column++)
            {
                value += _inverse[(row * count) + column] * destination[column];
            }

            if (value is < 0 or > 1)
                return false;
        }

        return true;
    }

    private static double[] ReadColorants(Dictionary<string, ReadOnlyMemory<byte>> tags)
    {
        var matrix = new double[9];
        string[] signatures = ["rXYZ", "gXYZ", "bXYZ"];
        for (var column = 0; column < 3; column++)
        {
            var tag = tags[signatures[column]].Span;
            for (var row = 0; row < 3; row++)
            {
                matrix[(row * 3) + column] = BinaryPrimitives.ReadInt32BigEndian(tag[(8 + (4 * row))..]) / 65536.0;
            }
        }

        return matrix;
    }

    private static double[] Invert(double[] m)
    {
        var determinant = (m[0] * ((m[4] * m[8]) - (m[5] * m[7]))) - (m[1] * ((m[3] * m[8]) - (m[5] * m[6]))) + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        Assert.True(Math.Abs(determinant) > 1e-9, "The colorants of the destination profile are singular.");
        return
        [
            ((m[4] * m[8]) - (m[5] * m[7])) / determinant, ((m[2] * m[7]) - (m[1] * m[8])) / determinant, ((m[1] * m[5]) - (m[2] * m[4])) / determinant,
            ((m[5] * m[6]) - (m[3] * m[8])) / determinant, ((m[0] * m[8]) - (m[2] * m[6])) / determinant, ((m[2] * m[3]) - (m[0] * m[5])) / determinant,
            ((m[3] * m[7]) - (m[4] * m[6])) / determinant, ((m[1] * m[6]) - (m[0] * m[7])) / determinant, ((m[0] * m[4]) - (m[1] * m[3])) / determinant,
        ];
    }
}
