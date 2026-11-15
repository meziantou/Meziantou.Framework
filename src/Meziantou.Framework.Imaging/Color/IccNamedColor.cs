using System.Collections.ObjectModel;

namespace Meziantou.Framework.Imaging;

/// <summary>A color of an ICC named color profile: its name, its colorimetry and the device values that reproduce it.</summary>
/// <remarks>
/// The colorimetry is media-relative, in the profile connection space: CIEXYZ relative to the D50 illuminant
/// (Y = 1 for the media white) and the CIELAB values of the same color relative to that illuminant. Instances are immutable.
/// </remarks>
public sealed class IccNamedColor
{
    internal IccNamedColor(string name, CieXyz connectionSpaceColor, CieLab connectionSpaceLab, double[] deviceValues)
    {
        Name = name;
        ConnectionSpaceColor = connectionSpaceColor;
        ConnectionSpaceLab = connectionSpaceLab;
        DeviceValues = Array.AsReadOnly(deviceValues);
    }

    /// <summary>
    /// Gets the name of the color: the prefix, the root name and the suffix of a version 2 or 4 profile, concatenated as
    /// they are stored (<c>"PMS "</c>, <c>"185"</c> and <c>" C"</c> give <c>"PMS 185 C"</c>), or the name of a version 5
    /// (iccMAX) profile.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the media-relative colorimetry of the color: CIEXYZ relative to the D50 illuminant of the profile connection space (Y = 1 for the media white).</summary>
    public CieXyz ConnectionSpaceColor { get; }

    /// <summary>Gets the media-relative colorimetry of the color as CIELAB relative to the D50 illuminant of the profile connection space.</summary>
    public CieLab ConnectionSpaceLab { get; }

    /// <summary>
    /// Gets the device values that reproduce the color on the device of the profile, normalized to [0, 1] for the colorants
    /// of its data color space (0 is no colorant); empty when the profile does not give them.
    /// </summary>
    public ReadOnlyCollection<double> DeviceValues { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
