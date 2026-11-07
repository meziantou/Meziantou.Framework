namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The parameters of the weighted (self-correcting) predictor (ISO/IEC 18181-1, section H.5.1).</summary>
internal sealed class JpegXlWeightedPredictorHeader
{
    private JpegXlWeightedPredictorHeader()
    {
    }

    /// <summary>Gets the default parameters.</summary>
    public static JpegXlWeightedPredictorHeader Default { get; } = new();

    public int P1 { get; private init; } = 16;

    public int P2 { get; private init; } = 10;

    public int P3A { get; private init; } = 7;

    public int P3B { get; private init; } = 7;

    public int P3C { get; private init; } = 7;

    public int P3D { get; private init; }

    public int P3E { get; private init; }

    /// <summary>Gets the weight of each of the four sub-predictors.</summary>
    public uint[] Weights { get; private init; } = [13, 12, 12, 12];

    public static JpegXlWeightedPredictorHeader Read(JpegXlBitReader reader)
    {
        if (reader.ReadBool())
            return Default;

        return new JpegXlWeightedPredictorHeader
        {
            P1 = (int)reader.ReadBits(5),
            P2 = (int)reader.ReadBits(5),
            P3A = (int)reader.ReadBits(5),
            P3B = (int)reader.ReadBits(5),
            P3C = (int)reader.ReadBits(5),
            P3D = (int)reader.ReadBits(5),
            P3E = (int)reader.ReadBits(5),
            Weights = [reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4), reader.ReadBits(4)],
        };
    }
}
