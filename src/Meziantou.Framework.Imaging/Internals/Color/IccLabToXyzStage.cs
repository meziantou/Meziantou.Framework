namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Converts CIELAB (L* in [0, 100]) to CIEXYZ relative to the D50 illuminant of the connection space.</summary>
internal sealed class IccLabToXyzStage : IccStage
{
    public static IccLabToXyzStage Instance { get; } = new();

    public override void Apply(Span<double> values)
    {
        var fy = (values[0] + 16) / 116;
        var fx = fy + (values[1] / 500);
        var fz = fy - (values[2] / 200);
        values[0] = IccColorimetry.D50X * IccColorimetry.LabInverse(fx);
        values[1] = IccColorimetry.D50Y * IccColorimetry.LabInverse(fy);
        values[2] = IccColorimetry.D50Z * IccColorimetry.LabInverse(fz);
    }
}
