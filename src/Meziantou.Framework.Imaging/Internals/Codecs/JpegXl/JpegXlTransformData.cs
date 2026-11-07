namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <c>CustomTransformData</c> bundle that follows the image metadata (ISO/IEC 18181-1, section A.6): the parameters of
/// the inverse XYB transform and the weights of the upsampling filters, when they are not the default ones.
/// </summary>
internal sealed class JpegXlTransformData
{
    private JpegXlTransformData()
    {
    }

    /// <summary>Gets the data of a header that only sets <c>all_default</c>.</summary>
    public static JpegXlTransformData Default { get; } = new();

    /// <summary>Gets the inverse opsin absorbance matrix, row by row.</summary>
    public float[] OpsinInverseMatrix { get; private init; } =
    [
        11.031566901960783f, -9.866943921568629f, -0.16462299647058826f,
        -3.254147380392157f, 4.418770392156863f, -0.16462299647058826f,
        -3.6588512862745097f, 2.7129230470588235f, 1.9459282392156863f,
    ];

    /// <summary>Gets the opsin biases of the three channels.</summary>
    public float[] OpsinBias { get; private init; } = [-0.0037930732552754493f, -0.0037930732552754493f, -0.0037930732552754493f];

    /// <summary>Gets the quantization biases of the three channels.</summary>
    public float[] QuantBias { get; private init; } = [1.0f - 0.05465007330715401f, 1.0f - 0.07005449891748593f, 1.0f - 0.049935103337343655f];

    /// <summary>Gets the numerator of the quantization bias of large coefficients.</summary>
    public float QuantBiasNumerator { get; private init; } = 0.145f;

    /// <summary>Gets the 15 custom weights of the 2x upsampling filter, or <see langword="null"/> for the default ones.</summary>
    public float[]? Upsampling2Weights { get; private init; }

    /// <summary>Gets the 55 custom weights of the 4x upsampling filter, or <see langword="null"/> for the default ones.</summary>
    public float[]? Upsampling4Weights { get; private init; }

    /// <summary>Gets the 210 custom weights of the 8x upsampling filter, or <see langword="null"/> for the default ones.</summary>
    public float[]? Upsampling8Weights { get; private init; }

    public static JpegXlTransformData Read(JpegXlBitReader reader, bool xybEncoded)
    {
        if (reader.ReadBool())
            return Default;

        var defaults = Default;
        var matrix = defaults.OpsinInverseMatrix;
        var opsinBias = defaults.OpsinBias;
        var quantBias = defaults.QuantBias;
        var quantBiasNumerator = defaults.QuantBiasNumerator;
        if (xybEncoded && !reader.ReadBool())
        {
            matrix = ReadArray(reader, 9);
            opsinBias = ReadArray(reader, 3);
            quantBias = ReadArray(reader, 3);
            quantBiasNumerator = reader.ReadF16();
        }

        var mask = reader.ReadBits(3);
        return new JpegXlTransformData
        {
            OpsinInverseMatrix = matrix,
            OpsinBias = opsinBias,
            QuantBias = quantBias,
            QuantBiasNumerator = quantBiasNumerator,
            Upsampling2Weights = (mask & 1) != 0 ? ReadArray(reader, 15) : null,
            Upsampling4Weights = (mask & 2) != 0 ? ReadArray(reader, 55) : null,
            Upsampling8Weights = (mask & 4) != 0 ? ReadArray(reader, 210) : null,
        };
    }

    private static float[] ReadArray(JpegXlBitReader reader, int count)
    {
        var values = new float[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadF16();
        }

        return values;
    }
}
