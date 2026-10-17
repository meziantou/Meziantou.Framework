namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Converts CIEXYZ relative to the D50 illuminant of the connection space to CIELAB.</summary>
internal sealed class IccXyzToLabStage : IccStage
{
    public static IccXyzToLabStage Instance { get; } = new();

    public override void Apply(Span<double> values)
    {
        var fx = IccColorimetry.LabFunction(values[0] / IccColorimetry.D50X);
        var fy = IccColorimetry.LabFunction(values[1] / IccColorimetry.D50Y);
        var fz = IccColorimetry.LabFunction(values[2] / IccColorimetry.D50Z);
        values[0] = (116 * fy) - 16;
        values[1] = 500 * (fx - fy);
        values[2] = 200 * (fy - fz);
    }
}
