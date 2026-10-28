namespace Meziantou.Framework.Imaging;

/// <summary>The weighting of the CIE 1994 color difference (<see cref="ColorDifference.DeltaE94"/>), which depends on the kind of samples compared.</summary>
public enum Cie94Application
{
    /// <summary>Graphic arts: kL = 1, K1 = 0.045, K2 = 0.015. This is the default.</summary>
    GraphicArts = 0,

    /// <summary>Textiles: kL = 2, K1 = 0.048, K2 = 0.014.</summary>
    Textiles = 1,
}
