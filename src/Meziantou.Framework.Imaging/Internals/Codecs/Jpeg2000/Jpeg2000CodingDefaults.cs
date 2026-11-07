namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The COD marker segment (T.800, A.6.1): the coding style of a tile, and the default coding style of its components.</summary>
internal sealed class Jpeg2000CodingDefaults
{
    public const int ProgressionLrcp = 0;
    public const int ProgressionRlcp = 1;
    public const int ProgressionRpcl = 2;
    public const int ProgressionPcrl = 3;
    public const int ProgressionCprl = 4;

    public Jpeg2000CodingDefaults(bool mayUseSop, bool usesEph, int progression, int layers, bool usesComponentTransform, Jpeg2000CodingStyle style)
    {
        MayUseSop = mayUseSop;
        UsesEph = usesEph;
        Progression = progression;
        Layers = layers;
        UsesComponentTransform = usesComponentTransform;
        Style = style;
    }

    /// <summary>Gets a value indicating whether a packet may be preceded by a SOP marker segment.</summary>
    public bool MayUseSop { get; }

    /// <summary>Gets a value indicating whether every packet header is followed by an EPH marker.</summary>
    public bool UsesEph { get; }

    /// <summary>Gets the progression order, one of the <c>Progression</c> constants.</summary>
    public int Progression { get; }

    /// <summary>Gets the number of layers, 1 to 65535.</summary>
    public int Layers { get; }

    /// <summary>Gets a value indicating whether the first three components went through the multiple component transformation.</summary>
    public bool UsesComponentTransform { get; }

    /// <summary>Gets the coding style of the components that have no COC marker segment.</summary>
    public Jpeg2000CodingStyle Style { get; }
}
