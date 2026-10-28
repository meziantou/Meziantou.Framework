namespace Meziantou.Framework.Imaging;

/// <summary>A color vision deficiency simulated by <see cref="ColorMatrix.CreateColorBlindness(ColorBlindnessType)"/>.</summary>
/// <remarks>
/// A dichromacy (<c>-opia</c>) is the lack of one of the three kinds of cones; an anomalous trichromacy (<c>-anomaly</c>)
/// is a shift of their sensitivity, whose severity goes from normal vision to the dichromacy.
/// </remarks>
public enum ColorBlindnessType
{
    /// <summary>No long-wavelength (red) cones: reds look darker, and red and green are confused.</summary>
    Protanopia = 0,

    /// <summary>Long-wavelength (red) cones whose sensitivity is shifted: a weaker <see cref="Protanopia"/>.</summary>
    Protanomaly = 1,

    /// <summary>No medium-wavelength (green) cones: red and green are confused. The most common deficiency.</summary>
    Deuteranopia = 2,

    /// <summary>Medium-wavelength (green) cones whose sensitivity is shifted: a weaker <see cref="Deuteranopia"/>.</summary>
    Deuteranomaly = 3,

    /// <summary>No short-wavelength (blue) cones: blue and green, and yellow and violet, are confused.</summary>
    Tritanopia = 4,

    /// <summary>Short-wavelength (blue) cones whose sensitivity is shifted: a weaker <see cref="Tritanopia"/>.</summary>
    Tritanomaly = 5,
}
